
using UnityEngine;
using UnityEngine.EventSystems;
using static UnityEngine.UI.GridLayoutGroup;

//Attach to an object that has a Collider and it will allow drag and move and resize (by click and dragging on the lower right corner) with mouse
//Note: Works best if pivot is top left

//By Seth A. Robinson 2022

public class ObjectDrag : MonoBehaviour
{
    private Vector3 mousePosition;
    private Vector3 mouseOffset;
    private Vector3 newPosition;
    private bool dragging;
    private bool resizing;
    private Vector3 initialScale;
    private Vector3 newScale;
    Vector3 m_topLeftInitial;
    float m_cornerSizePercent = 0.2f;


    float initialDistance;
    Camera _cam;
    Collider entityCollider;
    GameObject m_resetIcon;
    public GameObject m_resizeIconPrefab;
    void Start()
    {
        _cam = RTUtil.FindObjectOrCreate("Camera").GetComponent<Camera>();
        entityCollider = gameObject.transform.GetComponentInChildren<Collider>();
        m_resetIcon = Instantiate(m_resizeIconPrefab, transform);

        SetupResizeIcon();
    }
     

    void SetupResizeIcon()
    {
         if (m_resetIcon == null) return;
        float iconSize = m_resetIcon.GetComponent<MeshRenderer>().bounds.size.x*0.5f;

        var vTempPos = entityCollider.bounds.max;
        vTempPos.x -= iconSize;
        vTempPos.y = entityCollider.bounds.min.y + iconSize;


        m_resetIcon.transform.position = vTempPos;
    }

    bool IsMouseOverUs()
    {
        // Create a ray from the mouse position in the direction of the camera
        Ray ray = _cam.ScreenPointToRay(Input.mousePosition);
        // Perform a raycast against the quad's collider
        RaycastHit hit;


        if (gameObject.transform.GetComponentInChildren<Collider>().Raycast(ray, out hit, Mathf.Infinity))
        {
            // If the raycast hits the quad, the mouse is over the quad
            return true;
        }
            return false;
    }

    Vector3 TopLeftPos()
    {
        Bounds bounds = entityCollider.bounds;
        Vector3 topLeft = bounds.min;
        topLeft.y = bounds.max.y;

        return topLeft;
    }

    void Update()
    {
        SetupResizeIcon();

      
        // If the right mouse button is pressed, start resizing
        if (Input.GetMouseButtonDown(0) && !dragging && !resizing && !EventSystem.current.IsPointerOverGameObject())
        {
            //are they going to drag or resize something?

            Ray ray = _cam.ScreenPointToRay(Input.mousePosition);
            // Perform a raycast against the entity's collider
            RaycastHit hit;
            if (entityCollider.Raycast(ray, out hit, Mathf.Infinity))
            {
                // If the raycast hits the entity, start resizing
                // Get the initial scale of the entity
                initialScale = transform.localScale;
                // Calculate the initial distance between the mouse and the corner
                m_topLeftInitial = TopLeftPos();
                m_topLeftInitial.z = ray.origin.z; //we don't care about z, so make sure they are the same
                initialDistance = Vector3.Distance(m_topLeftInitial, ray.origin);
                // Set the resizing flag to true
        
                Debug.Log("Top left: " + m_topLeftInitial + " Click pos: " + ray.origin + " Init dist: " + initialDistance);
                float xClickPercent = (ray.origin.x- entityCollider.bounds.min.x) / (entityCollider.bounds.max.x- entityCollider.bounds.min.x);
                float yClickPercent = (ray.origin.y - entityCollider.bounds.max.y) / (entityCollider.bounds.min.y - entityCollider.bounds.max.y);

                //Debug.Log("Clicked " + xClickPercent + " percent of X, " + yClickPercent + " of Y");
                if (xClickPercent > (1-m_cornerSizePercent) && yClickPercent > 1- m_cornerSizePercent)
                {
                    //they clicked the bottom right corner, let them drag size
                    resizing = true;

                } else
                {
                    //treat like a position move and drag
                    mousePosition = _cam.ScreenToWorldPoint(Input.mousePosition);
                    // Calculate the offset between the mouse position and the entity's position
                    mouseOffset = transform.position - mousePosition;
                    // Set the dragging flag to true
                    dragging = true;
                }

            }
        }

          if (Input.GetMouseButtonUp(0))
          {
                // Set the dragging flag to false
                dragging = false;
                resizing = false;
          }

// If the entity is being dragged, update its position
        if (dragging)
        {
            // Get the mouse position in world space
            mousePosition = _cam.ScreenToWorldPoint(Input.mousePosition);
            // Calculate the new position for the entity using the mouse offset
            newPosition = mousePosition + mouseOffset;
            // Update the entity's position
            transform.position = newPosition;
        }

        // If the entity is being resized, update its scale
        if (resizing)
        {
            // Create a ray from the mouse position in the direction of the camera
            Ray ray = _cam.ScreenPointToRay(Input.mousePosition);
            // Calculate the new distance between the mouse and the corner
            float newDistance = Vector3.Distance(m_topLeftInitial, ray.origin);

           // Debug.Log("Dist: " + newDistance);
            // Calculate the scale factor based on the change in distance
            
            float scaleFactor = (newDistance) / initialDistance;
           
            if (scaleFactor < 0.1f)
            {
                scaleFactor = 0.1f;
            }
            // Calculate the new scale for the entity
            newScale = initialScale * scaleFactor;
            // Update the entity's scale
            transform.localScale = newScale;
        }
        
    }
}
