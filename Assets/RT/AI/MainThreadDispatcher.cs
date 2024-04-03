using System.Collections.Concurrent;
using System;
using UnityEngine;

/* simple thing to allow us to que up things to do from a task and have it be done later in the main thread

Note:  You need to attach this script to a gameobject somewhere

Example:

MainThreadDispatcher.Enqueue(() =>
{
    // Assuming you have a reference to your textbox UI component
    yourTextboxUIComponent.text += text;
});

*/


public class MainThreadDispatcher : MonoBehaviour
{
    private static readonly ConcurrentQueue<Action> actions = new ConcurrentQueue<Action>();

    private void Update()
    {
        while (actions.TryDequeue(out var action))
        {
            action?.Invoke();
        }
    }

    public static void Enqueue(Action action)
    {
        if (action == null) return;
        actions.Enqueue(action);
    }
}
