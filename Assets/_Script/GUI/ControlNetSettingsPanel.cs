using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ControlNetSettingsPanel : MonoBehaviour
{
    public TMP_Dropdown m_preprocessorDropdown;
    public TMP_Dropdown m_modelDropdown;
    public Slider m_weightSlider;
    public Slider m_guidanceSlider;


    // Start is called before the first frame update
    void Start()
    {
        //fill in default values
        m_preprocessorDropdown.ClearOptions();
        m_preprocessorDropdown.AddOptions(GameLogic.Get().GetControlNetPreprocessorArray());
        m_preprocessorDropdown.value = GameLogic.Get().GetCurrentControlNetPreprocessorIndex();

        m_modelDropdown.ClearOptions();
        m_modelDropdown.AddOptions(GameLogic.Get().GetControlNetModelArray());
        m_modelDropdown.value = GameLogic.Get().GetCurrentControlNetModelIndex();

        m_weightSlider.GetComponent<ControlNetWeightSlider>().UpdateValue(GameLogic.Get().GetControlNetWeight());
        m_guidanceSlider.GetComponent<ControlNetGuidanceSlider>().UpdateValue(GameLogic.Get().GetControlNetGuidance());

    }

    public void OnCurrentControlNetPreprocessorStringChanged(int index)
    {
        GameLogic.Get().OnCurrentControlNetPreprocessorStringChanged(index);
    }

    public void OnCurrentControlNetModelStringChanged(int index)
    {
        GameLogic.Get().OnCurrentControlNetModelStringChanged(index);
    }




    private void OnDestroy()
    {

    }
    // Update is called once per frame

    void Update()
    {
      
    }
}
