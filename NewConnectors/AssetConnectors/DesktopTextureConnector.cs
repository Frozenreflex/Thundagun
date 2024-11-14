using System;
using Elements.Core;
using FrooxEngine;
using UnityEngine;
using UnityFrooxEngineRunner;
using HarmonyLib;
using uDesktopDuplication;
using uWindowCapture;
using Texture = UnityEngine.Texture;
using System.Reflection;
using System.Collections.Generic;

namespace Thundagun.NewConnectors.AssetConnectors;

public class DesktopUpdatePacket : UpdatePacket<DuplicableDisplay>
{
    DuplicableDisplay Display;
    uDesktopDuplication.Monitor Monitor;
	static Type stateType;
	static FieldInfo currentStateField;
	List<Action> _requests;
	uDesktopDuplication.Monitor _monitor;
	object currentState;
	UwcWindow _window;
	enum State2
	{
		DirectCapture,
		WaitingOnWindow,
		WaitingOnTexture,
		UsingWindowCapture
	}
    public DesktopUpdatePacket(DuplicableDisplay owner, uDesktopDuplication.Monitor monitor) : base(owner)
    {
        Display = owner;
        Monitor = monitor;
		stateType ??= Display.currentState.GetType();
		currentStateField ??= AccessTools.Field(Display.GetType(), "currentState");
		_requests = Display._requests;
		_monitor = Display._monitor;
		_window = Display._window;
		currentState = Display.currentState;
    }

    public override void Update()
	{
		//Thundagun.Msg("DesktopUpdatePacket Update start");
        if (_requests.Count == 0)
		{
			//Thundagun.Msg("Requests count 0");
			Display._monitor = null;
			Display._window = null;
			Display.UpdateProperties(Monitor);
			return;
		}
		bool flag = false;
		if (Monitor != _monitor || ((int)currentState != 0 && (int)currentState != (int)State2.UsingWindowCapture))
		{
			if ((int)currentState != 0)
			{
				UniLog.Log($"Monitor {Monitor.id}, name: {Monitor.name}, state: {Monitor.state}");
			}
			if (Monitor.state == DuplicatorState.Unsupported)
			{
				//Thundagun.Msg("unsupported");
				_ = UwcManager.instance;
				Display._window = UwcManager.Find(Monitor.name, isAltTabWindow: false);
				if (Display._window != null)
				{
					currentStateField.SetValue(Display, Enum.ToObject(stateType, (int)State2.WaitingOnTexture));
					Display._window.captureMode = CaptureMode.BitBlt;
					Display._window.cursorDraw = true;
					UniLog.Log("Using fallback window capture: " + Display._window?.id);
				}
				else
				{
					currentStateField.SetValue(Display, Enum.ToObject(stateType, (int)State2.WaitingOnWindow));
				}
			}
			else
			{
				currentStateField.SetValue(Display, Enum.ToObject(stateType, (int)State2.DirectCapture));
				Display._window = null;
			}
			Display._monitor = Monitor;
			flag = true;
			if (_requests.Count > 0 && _window == null && (int)currentState == (int)State2.DirectCapture)
			{
				//Thundagun.Msg("creating texture if needed");
				Display._monitor.CreateTextureIfNeeded();
			}
		}
		if ((int)currentState != (int)State2.WaitingOnWindow)
		{
			if (_window != null)
			{
				//Thundagun.Msg("requesting capture");
				Display._window.RequestCapture();
			}
			else
			{
				//Thundagun.Msg("rendering");
				Display._monitor.Render();
			}
			//Thundagun.Msg("calling desktop rendered");
			Engine.Current.DesktopRendered();
		}
		if ((int)currentState == (int)State2.WaitingOnTexture && _window?.texture != null)
		{
			flag = true;
			currentStateField.SetValue(Display, Enum.ToObject(stateType, (int)State2.UsingWindowCapture));
		}
		Display.UpdateProperties(Monitor);
		if (!flag || ((int)currentState != 0 && (int)currentState != (int)State2.UsingWindowCapture))
		{
			return;
		}
		foreach (Action request in _requests)
		{
			//Thundagun.Msg("request invoke");
			request();
		}
    }
}

public class DesktopTextureConnector :
    AssetConnector,
    IDesktopTextureConnector,
    IUnityTextureProvider
{
    private IDisplayTextureSource _lastSource;
    private Action _onUpdated;

    public int2 Size
    {
        get
        {
            var unityTexture1 = UnityTexture;
            var width = unityTexture1 != null ? unityTexture1.width : 0;
            var unityTexture2 = UnityTexture;
            var height = unityTexture2 != null ? unityTexture2.height : 0;
            return new int2(width, height);
        }
    }

    public Texture UnityTexture => (_lastSource as IUnityTextureProvider)?.UnityTexture;

    public void Update(int index, Action onUpdated)
    {
        var screen = Engine.InputInterface.TryGetDisplay(index) as IDisplayTextureSource;
        if (screen == _lastSource)
            return;
        FreeSource();
        _onUpdated = onUpdated;
        if (screen != null)
            UnityAssetIntegrator.EnqueueProcessing(() =>
            {
                _lastSource = screen;
                screen.RegisterRequest(TextureUpdated);
                _onUpdated();
            }, true);
        else
            onUpdated();
    }

    public override void Unload() => FreeSource();

    private void FreeSource()
    {
        var source = _lastSource;
        _lastSource = null;
        _onUpdated = null;
        if (source == null)
            return;
        UnityAssetIntegrator.EnqueueProcessing(() => source.UnregisterRequest(TextureUpdated), true);
    }

    private void TextureUpdated()
    {
        var onUpdated = _onUpdated;
        onUpdated?.Invoke();
    }
}