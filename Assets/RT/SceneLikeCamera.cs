//Credit, from KampinKarl1 at https://github.com/KampinKarl1/Scene-View-Camera-in-Play-Mode/blob/master/SceneLikeCamera.cs

using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using TMPro;
using UnityEngine.UI;




public class SceneLikeCamera : MonoBehaviour
{
    [Header("Focus Object")]
    [SerializeField, Tooltip ("Enable double-click to focus on objects?")] 
    private bool doFocus = false;
    [SerializeField] private float focusLimit = 100f;
    [SerializeField] private float minFocusDistance = 5.0f;
    private float doubleClickTime = .15f;
    private float cooldown = 0;
    [Header("Undo - Only undoes the Focus Object - The keys must be pressed in order.")]
    [SerializeField] private KeyCode firstUndoKey = KeyCode.LeftControl;
    [SerializeField] private KeyCode secondUndoKey = KeyCode.Z;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 1.0f;
    [SerializeField] private float rotationSpeed = 10.0f;
    [SerializeField] private float zoomSpeed = 10.0f;

    //Cache last pos and rot be able to undo last focus object action.
    Quaternion prevRot = new Quaternion();
    Vector3 prevPos = new Vector3();

    [Header("Axes Names")]
    [SerializeField, Tooltip("Otherwise known as the vertical axis")] private string mouseY = "Mouse Y";
    [SerializeField, Tooltip("AKA horizontal axis")] private string mouseX = "Mouse X";
    [SerializeField, Tooltip("The axis you want to use for zoom.")] private string zoomAxis = "Mouse ScrollWheel";

    [Header("Move Keys")]
    [SerializeField] private KeyCode forwardKey = KeyCode.W;
    [SerializeField] private KeyCode backKey = KeyCode.S;
    [SerializeField] private KeyCode leftKey = KeyCode.A;
    [SerializeField] private KeyCode rightKey = KeyCode.D;
    [SerializeField] private KeyCode forwardKey2 = KeyCode.UpArrow;
    [SerializeField] private KeyCode backKey2 = KeyCode.DownArrow;
    [SerializeField] private KeyCode leftKey2 = KeyCode.LeftArrow;
    [SerializeField] private KeyCode rightKey2 = KeyCode.RightArrow;

    [Header("Flat Move"), Tooltip("Instead of going where the camera is pointed, the camera moves only on the horizontal plane (Assuming you are working in 3D with default preferences).")]
    [SerializeField] private KeyCode flatMoveKey = KeyCode.LeftShift;

    [Header("Anchored Movement"), Tooltip("By default in scene-view, this is done by right-clicking for rotation or middle mouse clicking for up and down")]
    [SerializeField] private KeyCode anchoredMoveKey = KeyCode.Mouse2;

    [SerializeField] private KeyCode anchoredRotateKey = KeyCode.Mouse1;

    private void Start()
    {
        SavePosAndRot();
    }

    void Update()
    {
        if (!doFocus)
            return;

        //Double click for focus 
        if (cooldown > 0 && Input.GetKeyDown(KeyCode.Mouse0))
            FocusObject();
        if (Input.GetKeyDown(KeyCode.Mouse0))
            cooldown = doubleClickTime;

        //--------UNDO FOCUS---------
        if (Input.GetKey(firstUndoKey)) 
        {
            if (Input.GetKeyDown(secondUndoKey))
                GoBackToLastPosition();
        }

        cooldown -= Time.deltaTime;
    }

    private void LateUpdate()
    {
      
        //Seth added this code so we won't fly around with wasd while doing text input
        var selectedObject = FindObjectOfType<EventSystem>().currentSelectedGameObject;

        if (selectedObject)
        {
            if (
                (selectedObject.GetComponent<TMPro.TMP_InputField>() != null)
                || (selectedObject.GetComponent<InputField>() != null)
                )
           {
                return;
            } else
            {
             //Debug.Log("Not input field, it's a " + selectedObject.name);
            }
        }

        Vector3 move = Vector3.zero;
        
        //Move and rotate the camera
    
        if (Input.GetKey(forwardKey) || Input.GetKey(forwardKey2))
            move += Vector3.forward * moveSpeed;
        if (Input.GetKey(backKey) || Input.GetKey(backKey2))
            move += Vector3.back * moveSpeed;
        if (Input.GetKey(leftKey)|| Input.GetKey(leftKey2))
            move += Vector3.left * moveSpeed;
        if (Input.GetKey(rightKey)|| Input.GetKey(rightKey2))
            move += Vector3.right * moveSpeed;

        //By far the simplest solution I could come up with for moving only on the Horizontal plane - no rotation, just cache y
        if (Input.GetKey(flatMoveKey))
        {
            float origY = transform.position.y;

            transform.Translate(move);
            transform.position = new Vector3(transform.position.x, origY, transform.position.z);

            return;
        }

        float mouseMoveY = Input.GetAxis(mouseY);
        float mouseMoveX = Input.GetAxis(mouseX);

        //Move the camera when anchored
        if (Input.GetKey(anchoredMoveKey)) 
        {
            move += Vector3.up * mouseMoveY * -moveSpeed;
            move += Vector3.right * mouseMoveX * -moveSpeed;
        }

        //Rotate the camera when anchored
        if (Input.GetKey(anchoredRotateKey)) 
        {
            transform.RotateAround(transform.position, transform.right, mouseMoveY * -rotationSpeed);
            transform.RotateAround(transform.position, Vector3.up, mouseMoveX * rotationSpeed);
        }

        transform.Translate(move);
        
        //Scroll to zoom
        float mouseScroll = Input.GetAxis(zoomAxis);
        transform.Translate(Vector3.forward * mouseScroll * zoomSpeed);
    }

    private void FocusObject()
    {
        //To be able to undo
        SavePosAndRot();

        //If we double-clicked an object in the scene, go to its position
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, focusLimit))
        {
            GameObject target = hit.collider.gameObject;
            Vector3 targetPos = target.transform.position;
            Vector3 targetSize = hit.collider.bounds.size;

            transform.position = targetPos + GetOffset(targetPos, targetSize);

            transform.LookAt(target.transform);
        }
    }

    private void SavePosAndRot() 
    {
        prevRot = transform.rotation;
        prevPos = transform.position;
    }

    private void GoBackToLastPosition() 
    {
        transform.position = prevPos;
        transform.rotation = prevRot;
    }

    private Vector3 GetOffset(Vector3 targetPos, Vector3 targetSize)
    {
        Vector3 dirToTarget = targetPos - transform.position;

        float focusDistance = Mathf.Max(targetSize.x, targetSize.z);
        focusDistance = Mathf.Clamp(focusDistance, minFocusDistance, focusDistance);

        return -dirToTarget.normalized * focusDistance;
    }
}