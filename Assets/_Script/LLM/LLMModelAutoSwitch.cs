using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Recovers from "the model X does not exist" on OpenAI Compatible instances
/// without a trip to LLM Settings: when a request fails that way, the request
/// manager asks this class to fetch the server's live "/v1/models" list, and if
/// the saved model is really gone the matching instance(s) are switched to the
/// FIRST model the server lists, config_llm.txt is saved, everyone is told
/// (toast + console + the Switched event, which AI Chat renders as an always-
/// visible Notice bubble), and the manager retries the request with the new
/// model. If the saved model IS in the list, or the list cannot be fetched,
/// nothing is changed and the original error is surfaced as before.
///
/// Scope: only instances whose provider is OpenAICompatible and whose endpoint
/// (with replica port offsets) is the server that answered. Cloud OpenAI and
/// the other providers are untouched - their model lists come from
/// model_data.json, and a 404 there is a real configuration problem.
///
/// Pure detection/rewriting helpers live in LLMModelNotFound (Assets/RT/AI),
/// which is what the console regression test covers.
/// </summary>
public static class LLMModelAutoSwitch
{
    public class SwitchResult
    {
        public string oldModel = "";
        public string newModel = "";
        public string baseUrl = "";
        public List<string> models = new List<string>();
        public List<LLMInstanceInfo> instances = new List<LLMInstanceInfo>();
        /// <summary>Human-readable notice (plain text, no markup) describing the switch.</summary>
        public string notice = "";
    }

    /// <summary>Fired (main thread) after an instance's model was auto-switched and saved.</summary>
    public static event Action<SwitchResult> Switched;

    // One fetch per server at a time: a main turn and a sidecar can fail on the
    // same stale model in the same frame, and they should share one /v1/models
    // round trip and one switch.
    private static readonly Dictionary<string, List<Action<string>>> _inFlight =
        new Dictionary<string, List<Action<string>>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Instances this request could have come from: OpenAICompatible provider and
    /// an endpoint (any replica) on the same server base as requestEndpoint.
    /// </summary>
    public static List<LLMInstanceInfo> FindInstancesForEndpoint(string requestEndpoint)
    {
        var result = new List<LLMInstanceInfo>();
        var mgr = LLMInstanceManager.Get();
        if (mgr == null || string.IsNullOrEmpty(requestEndpoint)) return result;

        foreach (var inst in mgr.GetAllInstances())
        {
            if (inst == null || inst.settings == null) continue;
            if (inst.providerType != LLMProvider.OpenAICompatible) continue;
            if (string.IsNullOrEmpty(inst.settings.endpoint)) continue;

            int replicas = inst.GetEffectiveReplicaCount();
            for (int r = 0; r < replicas; r++)
            {
                string ep = LLMInstanceManager.ApplyReplicaPortOffset(inst.settings.endpoint, r);
                if (LLMModelNotFound.SameServerBase(ep, requestEndpoint))
                {
                    result.Add(inst);
                    break;
                }
            }
        }
        return result;
    }

    /// <summary>
    /// Called by the request manager after a model-not-found error. onDone receives
    /// the model to retry with, or null when no switch happened (unknown server,
    /// list fetch failed, empty list, or the requested model is actually listed).
    /// </summary>
    public static void TryResolve(string requestEndpoint, string apiKey, string requestedModel, Action<string> onDone)
    {
        onDone = onDone ?? (_ => { });

        var instances = FindInstancesForEndpoint(requestEndpoint);
        if (instances.Count == 0)
        {
            RTConsole.Log($"LLMModelAutoSwitch: model '{requestedModel}' rejected by {requestEndpoint}, but no OpenAI Compatible instance uses that server - not switching.");
            onDone(null);
            return;
        }

        string baseUrl = LLMModelNotFound.ChatEndpointToBaseUrl(requestEndpoint);
        if (_inFlight.TryGetValue(baseUrl, out var waiters))
        {
            waiters.Add(onDone);
            return;
        }
        waiters = new List<Action<string>> { onDone };
        _inFlight[baseUrl] = waiters;

        RTConsole.Log($"LLMModelAutoSwitch: model '{requestedModel}' not found on {baseUrl}; fetching the server's model list...");

        LlamaCppModelFetcher.FetchOpenAICompatibleModels(baseUrl, apiKey, (modelsInfo, error) =>
        {
            string newModel = null;
            try
            {
                newModel = ApplyFetchedList(instances, baseUrl, requestedModel, modelsInfo, error);
            }
            catch (Exception e)
            {
                Debug.LogError("LLMModelAutoSwitch: " + e);
            }

            _inFlight.Remove(baseUrl);
            foreach (var cb in waiters)
            {
                try { cb(newModel); }
                catch (Exception e) { Debug.LogError("LLMModelAutoSwitch callback threw: " + e); }
            }
        });
    }

    private static string ApplyFetchedList(List<LLMInstanceInfo> instances, string baseUrl, string requestedModel,
        LlamaCppModelFetcher.LlamaCppModelsInfo modelsInfo, string error)
    {
        if (!string.IsNullOrEmpty(error) || modelsInfo == null || modelsInfo.modelIds.Count == 0)
        {
            string why = !string.IsNullOrEmpty(error) ? error : "the server listed no models";
            RTConsole.Log($"LLMModelAutoSwitch: could not auto-switch on {baseUrl}: {why}");
            return null;
        }

        var models = new List<string>(modelsInfo.modelIds);
        if (!string.IsNullOrEmpty(requestedModel) && models.Contains(requestedModel))
        {
            // The server knows the model after all - the 404 means something else
            // (a router that failed to load it, an auth-scoped list, ...). Leave the
            // selection alone so the real error reaches the user.
            RTConsole.Log($"LLMModelAutoSwitch: '{requestedModel}' IS in {baseUrl}'s model list ({models.Count} model(s)); not switching.");
            return null;
        }

        string newModel = models[0];
        var mgr = LLMInstanceManager.Get();
        mgr?.ApplyModelAutoSwitch(instances, models, newModel);

        var names = new List<string>();
        foreach (var inst in instances)
            names.Add(string.IsNullOrEmpty(inst.name) ? $"instance {inst.instanceID}" : inst.name);
        string instanceList = string.Join(", ", names);
        string oldName = string.IsNullOrEmpty(requestedModel) ? "(none)" : requestedModel;
        string countNote = models.Count == 1
            ? "the only model it serves"
            : $"the first of {models.Count} models it lists";

        var result = new SwitchResult
        {
            oldModel = requestedModel ?? "",
            newModel = newModel,
            baseUrl = baseUrl,
            models = models,
            instances = instances,
            notice = $"LLM model \"{oldName}\" no longer exists on {instanceList} ({baseUrl}). " +
                     $"Auto-switched to \"{newModel}\" ({countNote}), saved it to LLM Settings, and retried the request " +
                     "(this one reply uses the server's default reasoning settings; later turns use the new model's own)."
        };

        RTConsole.Log("LLMModelAutoSwitch: " + result.notice);
        Debug.Log("LLMModelAutoSwitch: " + result.notice);
        try { RTQuickMessageManager.Get()?.ShowMessage($"LLM model \"{oldName}\" is gone - auto-switched {instanceList} to \"{newModel}\"", 6f); }
        catch { /* no toast manager in this scene */ }

        try { Switched?.Invoke(result); }
        catch (Exception e) { Debug.LogError("LLMModelAutoSwitch.Switched handler threw: " + e); }

        return newModel;
    }
}
