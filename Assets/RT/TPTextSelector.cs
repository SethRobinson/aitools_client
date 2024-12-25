using UnityEngine;
using TMPro;
using UnityEngine.EventSystems;
using System.Text;

public class TMPTextSelector : MonoBehaviour
{
    public TextMeshPro textMeshPro;
    public Camera mainCamera;

    private int startIndex = -1;
    private int endIndex = -1;
    private Color32 highlightColor = new Color32(255, 255, 0, 64); // Yellow with some transparency

    void Start()
    {
        if (textMeshPro == null)
        {
            textMeshPro = GetComponent<TextMeshPro>();
            if (textMeshPro == null)
                Debug.LogError("TextMeshPro component not found!");
        }

        if (mainCamera == null)
        {
            // Try to find the camera in multiple ways
            mainCamera = Camera.main;
            if (mainCamera == null)
                mainCamera = FindObjectOfType<Camera>();
            if (mainCamera == null)
                mainCamera = GameObject.FindGameObjectWithTag("MainCamera")?.GetComponent<Camera>();

            if (mainCamera == null)
                Debug.LogError("No camera found! Please assign a camera in the inspector.");
            else
                Debug.Log("Camera found: " + mainCamera.name);
        }
        else
        {
            Debug.Log("Camera already assigned: " + mainCamera.name);
        }
    }

    void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            startIndex = GetCharacterIndex();
            endIndex = startIndex;
        }
        else if (Input.GetMouseButton(0))
        {
            endIndex = GetCharacterIndex();
            HighlightText();
        }
        else if (Input.GetMouseButtonUp(0))
        {
            if (startIndex != -1 && endIndex != -1)
            {
                CopySelectedText();
            }
        }
    }

    int GetCharacterIndex()
    {
        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit))
        {
            if (hit.collider.gameObject == gameObject)
            {
                Vector3 localPoint = transform.InverseTransformPoint(hit.point);
                return TMP_TextUtilities.FindNearestCharacter(textMeshPro, localPoint, Camera.main, true);
            }
        }

        return -1;
    }

    void HighlightText()
    {
        textMeshPro.ForceMeshUpdate();

        TMP_TextInfo textInfo = textMeshPro.textInfo;
        Color32[] vertexColors = textInfo.meshInfo[0].colors32;

        int start = Mathf.Min(startIndex, endIndex);
        int end = Mathf.Max(startIndex, endIndex);

        for (int i = 0; i < textInfo.characterCount; i++)
        {
            if (i >= start && i <= end)
            {
                int vertexIndex = textInfo.characterInfo[i].vertexIndex;
                vertexColors[vertexIndex + 0] = highlightColor;
                vertexColors[vertexIndex + 1] = highlightColor;
                vertexColors[vertexIndex + 2] = highlightColor;
                vertexColors[vertexIndex + 3] = highlightColor;
            }
            else
            {
                int vertexIndex = textInfo.characterInfo[i].vertexIndex;
                vertexColors[vertexIndex + 0] = textInfo.characterInfo[i].color;
                vertexColors[vertexIndex + 1] = textInfo.characterInfo[i].color;
                vertexColors[vertexIndex + 2] = textInfo.characterInfo[i].color;
                vertexColors[vertexIndex + 3] = textInfo.characterInfo[i].color;
            }
        }

        textMeshPro.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
    }

    void CopySelectedText()
    {
        if (startIndex == -1 || endIndex == -1) return;

        int start = Mathf.Min(startIndex, endIndex);
        int end = Mathf.Max(startIndex, endIndex);

        StringBuilder sb = new StringBuilder();
        for (int i = start; i <= end; i++)
        {
            sb.Append(textMeshPro.text[i]);
        }

        GUIUtility.systemCopyBuffer = sb.ToString();
        Debug.Log("Copied text: " + sb.ToString());
    }
}