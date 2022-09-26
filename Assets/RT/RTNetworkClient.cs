/*
 * Written by Seth A. Robinson (rtsoft.com)
 * 
*/

//disable warnings about UNET being depreciated
#pragma warning disable 0618

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using System.Runtime.Serialization.Formatters.Binary;
using System.IO;
using System;

public class RTNetworkClient : MonoBehaviour, IRTNetworkConnection
{
    public Action<RTNetworkClient> OnConnectedEvent;
    public Action<RTNetworkClient> OnConnectFailedEvent;
    public Action<RTNetworkClient> OnDisconnectedEvent;
    public Action<RTNetworkClient, string> OnPacketStringEvent;
    public Action<RTNetworkClient, float> OnPacketPingEvent;
    public Action<RTNetworkClient, RTDB> OnPacketDBEvent;
    public delegate void Action<T1, T2, T3, T4, T5>(T1 arg, T2 arg2, T3 arg3, T4 arg4, T5 arg5);

    //void OnPacketEvent(RTNetworkClient client, short packetType, byte[] buffer, int size, int index)
    public event Action<RTNetworkClient, short, byte[], int, int> OnPacketEvent;

    public bool isConnected { get; private set; }

    public int GetHostID() { return _hostID; }
    public int GetConnectionID() { return _connectionID; }
    public IRTNetworkConnection GetServer() { return this; } //weird, but makes code more readable and understandable
    public void SetShowDebugMessages(bool bNew) { _shouldShowDebugMessages = bNew; }

    int _connectionID;
    int _hostID = -1;
    static byte[] _buffer;
    const int _bufferSize = 1024 * 63;
    bool _paused = false;

    string _connectASAPDestinationSite;
    int _connectASAPport;
    bool _shouldShowDebugMessages = false;
    float _timeOfLastPingSend = 0;

    void Awake()
    {
        _buffer = new byte[_bufferSize];
    }

    void OnDestroy()
    {

        Disconnect();
    }

    public void SetPause(bool bStart) //completely kill packet processing, only useful to simulate lag
    {

        _paused = bStart;
    }

    public bool GetPause()
    {
        return _paused;
    }

    //note: This is NOT a polite way to disconnect, we kill the whole port right after sending the request so I doubt it even gets sent.  However, your server DOES
    //need to handle this kind of disconnect so I didn't bother with a polite way for now

    public void Disconnect()
    {
        if (_hostID != -1)
        {
            if (_connectionID > 0)
            {

                Log("Disconnecting hostID " + _hostID + " client connectionID " + _connectionID);

                //if (NetworkTransport.ac)

                byte error;
                NetworkTransport.Disconnect(_hostID, _connectionID, out error);

                if ((NetworkError)error != NetworkError.Ok)
                {
                    Log("Error disconnecting connectionid " + _connectionID + " - Error " + (NetworkError)error);
                    OnConnectFailedEvent(this);
                }
            }

            NetworkTransport.RemoveHost(_hostID);
            _hostID = -1;
            _connectionID = 0;
            isConnected = false;
        }
        else
        {
            // Log("Not removing host, it's not initted");
        }

        //either way, init the whole thing again, otherwise we start getting "wronghost" errors after 12 connects

    }

    void Log(string text)
    {
        if (_shouldShowDebugMessages) RTConsole.Log(text);
    }

    public bool Connect(string destinationSite, int port)
    {
        _connectASAPDestinationSite = "";
        _connectASAPport = 0;
        byte error = 0;

        if (_hostID != -1)
        {
            Log("Client disconnecting before reconnecting... host " + _hostID);
            //well, we should disconnect first
            Disconnect();
        }

        try
        {
            Log("Connecting to " + destinationSite + " at " + port);

           _hostID = NetworkTransport.AddHost(RTNetworkManager.Get().topology, 0); //0 means we don't care
        }
        catch (System.Exception ex)
        {
            Debug.Log("RTNetworkClient.AddHost> " + ex.Message);
        }

        if (_hostID == -1)
        {

            Log("Error connecting to " + destinationSite + " at " + port + " - Error hostID is -1, reached maximum hosts probably");

            OnConnectFailedEvent(this);
            return false;
        }

        string ipAddress = "error";


#if !UNITY_WEBGL

        System.Net.IPAddress[] iplist = System.Net.Dns.GetHostAddresses(destinationSite);

        if (iplist == null || iplist.Length == 0)
        {

            Log("Can't convert " + iplist + " to an IP address!");
            OnConnectFailedEvent(this);
            return false;
        }

        foreach (System.Net.IPAddress address in iplist)
        {
            if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                //looks legit
                ipAddress = address.ToString();
            }
        }
#else
        //webGL coughs and dies on System.Net.Dns.GetHostAddresses so I hope you used the actual numeric IP address, fool
        
        if (destinationSite == "localhost")
        {
            destinationSite = "127.0.0.1";
        }
        ipAddress = destinationSite;

        
#endif

        try
        {
            _connectionID = NetworkTransport.Connect(_hostID, ipAddress, port, 0, out error);
        }
        catch (System.Exception ex)
        {
            Debug.Log("RTNetworkClient.Connect> " + ex.Message);
        }

        Log("Connecting to " + destinationSite + " ("+ipAddress+") :" + port + " with hostID "+_hostID+".  Was given connection id: " + _connectionID + ") ...");

        if ((NetworkError)error != NetworkError.Ok)
        {

            Log("Quick error: connecting to " + destinationSite + " at " + port + " - Error " + (NetworkError)error);

            OnConnectFailedEvent(this);
            return false;
        }

        return true; //success
    }


    public bool IsWaitingForPing()
    {
        return _timeOfLastPingSend != 0;
    }

    public void Ping()
    {
        if (_hostID < 0 || _connectionID <= 0) return;
        _timeOfLastPingSend = Time.time;
        RTNetworkManager.Get().SendPacketNoData(this, (short)RTNetworkManager.PacketType.PING);
    }

    // Update is called once per frame
    void Update()
    {

        if (_hostID == -1) return;

        if (_paused) return;


        int outConnectionId;
        int outChannelId;
        int receiveSize;
        byte error;
        NetworkEventType evnt;

        short packetType = 0;
        int index = 0;

        while ((evnt = NetworkTransport.ReceiveFromHost(_hostID, out outConnectionId, out outChannelId, _buffer, _bufferSize, out receiveSize, out error)) != NetworkEventType.Nothing)
        {

            if ((NetworkError)error != NetworkError.Ok)
            {
                Log("Client error getting packet from " + _hostID + " - Error: " + (NetworkError)error);
                if (OnDisconnectedEvent != null) OnDisconnectedEvent(this);

                //disconnect I guess.  This is possibly the wrong way to do it but...
                NetworkTransport.RemoveHost(_hostID);
                _hostID = -1;
                _connectionID = 0;
                break;
            }

            if (_connectionID != outConnectionId)
            {
                Log("Ignoring packet from old connection ID " + outConnectionId);
                continue;
            }

            if (_shouldShowDebugMessages)
            {
                //Log("Client: Got " + evnt + " outConnectionId: " + outConnectionId + ", size: " + receiveSize+" channel: "+outChannelId);
            }

            switch (evnt)
            {

                case NetworkEventType.DataEvent:
                    if (receiveSize < 6)
                    {
                        Log("Client: Bad data packet, only " + receiveSize + " bytes");
                        return;
                    }

                    int expectedSize = 0;
                    index = 0;
                    RTUtil.SerializeInt32(ref expectedSize, _buffer, ref index);

                    RTUtil.SerializeInt16(ref packetType, _buffer, ref index);

                    switch (packetType)
                    {
                        case (short)RTNetworkManager.PacketType.STRING:

                            string s = "";
                            RTUtil.SerializeString(ref s, _buffer, ref index);
                            OnPacketStringEvent(this, s);
                            break;

                        case (short)RTNetworkManager.PacketType.DB:

                            RTDB db = new RTDB();
                            int tempIndex = 0;
                            db.DeSerialize(_buffer,ref tempIndex);
                            if (OnPacketDBEvent != null)
                                OnPacketDBEvent(this, db);
                            else
                            {
                                print("Unhandled network packet DB");
                            }
                            break;


                        case (short)RTNetworkManager.PacketType.PING_RECEIVED:
                            float pingTime = Time.time - _timeOfLastPingSend;
                            _timeOfLastPingSend = 0;
                            OnPacketPingEvent(this, pingTime);
                            break;

                        default:
                            OnPacketEvent(this, packetType, _buffer, receiveSize, index);
                            break;

                    }

                    break;

                case NetworkEventType.ConnectEvent:
                    isConnected = true;

                    if (OnConnectedEvent != null)
                    {
                        //  print("Sending connect message..");
                        OnConnectedEvent(this);
                    }
                    break;

                case NetworkEventType.DisconnectEvent:

                    if (isConnected)
                    {
                        isConnected = false;

                        _connectionID = 0;

                        if (_connectASAPDestinationSite.Length > 0)
                        {
                            //we're about to connect again, don't bother with this event, it would be confused with the reconnect
                            Connect(_connectASAPDestinationSite, _connectASAPport);
                        }
                        else
                        {

                            if (OnDisconnectedEvent != null) OnDisconnectedEvent(this);
                        }
                    }
                    else
                    {

                        OnConnectFailedEvent(this);
                    }
                    break;
            }

        }

    }
}
