using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using System;

//Based on code from Xarbrough: https://answers.unity.com/questions/1226851/addlistener-to-onpointerdown-of-button-instead-of.html

// Button that raises onDown event when OnPointerDown is called.
[AddComponentMenu("RT/RTButton")]
public class RTButton : Button
{ 
    // Event delegate triggered on mouse or touch down.
    [SerializeField]
    ButtonDownEvent _onDown = new ButtonDownEvent();
    [SerializeField]
    ButtonUpEvent _onUp = new ButtonUpEvent();

    protected RTButton() { }

    public override void OnPointerDown(PointerEventData eventData)
    {
        base.OnPointerDown(eventData);

        if (eventData.button != PointerEventData.InputButton.Left)
            return;

        _onDown.Invoke();
    }

    public ButtonDownEvent onDown
    {
        get { return _onDown; }
        set { _onDown = value; }
    }

    public override void OnPointerUp(PointerEventData eventData)
    {
        base.OnPointerUp(eventData);

        if (eventData.button != PointerEventData.InputButton.Left)
            return;

        _onUp.Invoke();
    }

    public ButtonUpEvent onUp
    {
        get { return _onUp; }
        set { _onUp = value; }
    }

    [Serializable]
    public class ButtonDownEvent : UnityEvent { }
    [Serializable]
    public class ButtonUpEvent : UnityEvent { }
}