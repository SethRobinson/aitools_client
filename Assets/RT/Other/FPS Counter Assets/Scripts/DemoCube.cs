using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// This script is just instantiating random colored cube game objects.
// Feel free to use it in your own project if you have any need for.
// This script is not part of the FPS Counter itself. Both scripts are independent of each other!

public class DemoCube : MonoBehaviour {

    public Text m_quantityLabel = null;		// Text label for showing amount of instantiating game objects
    private static int m_quantity = 20;		// Quantity of instantiating game objects

    private void Start()
    {
	// If text label reference is missing, prompt errorr message and disable script
        if(m_quantityLabel==null)
        {
            Debug.LogError("Missing Text component reference! Disabling script!");
            this.enabled = false;
        }
	
	// Show quantity on UI text label
        m_quantityLabel.text = m_quantity.ToString();
    }
    
    // Left mouse button pressed on game object
    void OnMouseUpAsButton()
    {

        // Do some performance intensive stuff like creating game objects with enabled physics
        CreateGameObjects();

    }

    void CreateGameObjects()
    {
	    // The game objects get instantiated radial.
	    // Change the radius for the circle if you need to
        float t_radius = 3f;

	    // Loop to instantiate 'm_quantity' of game objects
        for (int t_count = 0; t_count < m_quantity; ++t_count)
        {
	        // Create a new cube game object
            GameObject t_newCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
	        // Change the cube's position
            t_newCube.transform.position += new Vector3(Mathf.Sin((t_count * (360/m_quantity))*Mathf.Deg2Rad)*t_radius, 3, Mathf.Cos((t_count*(360f/m_quantity))*Mathf.Deg2Rad)*t_radius);
            // Add a rigidbody component to the cube to get it effected by physics
            t_newCube.AddComponent<Rigidbody>();
            // Create a new material
            Material t_newColor = new Material(gameObject.GetComponent<Renderer>().material);
            // Put a random color to the material
            t_newColor.color = new Color(Random.Range(0f, 1f), Random.Range(0f, 1f), Random.Range(0f, 1f));
            // Assign new material to cube game object
            t_newCube.GetComponent<Renderer>().material = t_newColor;
            // Let the cube game object destroy right after it's living for 3 seconds
            Destroy(t_newCube, 3f);
        }
    }

    // Callback method for a slider to set quantity of game objects to instantiate
    public void SetQuantity(Slider sq_quantity)
    {
        // Assign new quantity to 'm_quantity'
        m_quantity = (int)sq_quantity.value;
        // Show new quantity on UI text label
        m_quantityLabel.text = m_quantity.ToString();
    }
}
