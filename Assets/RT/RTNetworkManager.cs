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

public interface IRTNetworkConnection
{
    int GetHostID();
    int GetConnectionID();
}


public class RTNetworkManager : MonoBehaviour
{
    public static int myReliableChannelId { get; private set; }
    public static int myUnreliableChannelId { get; private set; }

    public enum PacketType
    {
        PING,
        PING_RECEIVED,
        STRING,
        DB,
        RESERVED = 100 //add your own type starting at 101
    }
    
    ConnectionConfig config;
    static RTNetworkManager _this;
    public HostTopology topology;
    int _builtPacketSize = 0;
    public static RTNetworkManager Get() { return _this; }
    byte[] _buffer;
    int _bufferSize = 1024 * 63;
    bool _isInitted;

    void Awake()
    {
        _buffer = new byte[_bufferSize];
        //DontDestroyOnLoad(gameObject);
        _this = this;
    }

    public bool IsInitted()
    {
        return _isInitted;
    }

    void OnApplicationQuit()
    {
        // Make sure prefs are saved before quitting.
        //PlayerPrefs.Save();
        NetworkTransport.Shutdown();
        _isInitted = false;
    }

    // Use this for initialization
    public void InitNetworkStuff (int maxConnections, ushort maxHosts, bool setupAsServer)
    {

        // Initializing the Transport Layer with no arguments (default settings)
         GlobalConfig gConfig = new GlobalConfig();

        //gConfig.MaxPacketSize = 500; //Don't use custom settings unless you have to, says Unity

        if (setupAsServer)
        {
            //if you get "Error: Cannot allocate new packet for sending" suring burst sends you need to raise these.  The docs say they auto increase but it seems like they only do AFTER hitting the errors
            gConfig.ReactorMaximumSentMessages = 30000;
            gConfig.ReactorMaximumReceivedMessages = 30000;
            gConfig.ReactorModel = ReactorModel.SelectReactor;
        } else
        {
            
        }
      
        gConfig.ThreadAwakeTimeout = 1;

        gConfig.MaxHosts = maxHosts; //helps with webgl as re-using hosts causes webgl freezes for some reason!?
        RTConsole.Log("NetworkManager initializing...");

        //this throws a "Success" exception?!
        try
        {
            NetworkTransport.Init(gConfig);
        }
        catch (System.Exception ex)
        {
            Debug.Log("RTNetworkManager> "+ex.Message);
        }

        _isInitted = true;
        config = new ConnectionConfig();

        config.AcksType = ConnectionAcksType.Acks32; //NOTE: Client must match server or you'll get a CRC error on connection
        config.PingTimeout = 4000; //NOTE: Client must match server or you'll get a CRC error on connection
        config.DisconnectTimeout = 6000; //NOTE: Client must match server or you'll get a CRC error on connection

        if (setupAsServer)
        {
            config.MaxSentMessageQueueSize = 30000; //Um... is this per connection or?
        }

        config.MaxCombinedReliableMessageCount = 20; //0 for none?
        config.MaxCombinedReliableMessageSize = 100; //in bytes
        config.MinUpdateTimeout = 10;
        config.OverflowDropThreshold = 100;
        config.NetworkDropThreshold = 100;
        config.SendDelay = 10;
        
        myReliableChannelId = config.AddChannel(QosType.ReliableFragmentedSequenced);
        myUnreliableChannelId = config.AddChannel(QosType.Unreliable);
        topology = new HostTopology(config, maxConnections);

        if (setupAsServer)
            topology.SentMessagePoolSize = ushort.MaxValue; //this # is very important when bursting packets out

        /*
        RTConsole.Log("Bandwidth allowed: " + config.InitialBandwidth + " Send queue max: " + config.MaxSentMessageQueueSize + " Max hosts: " + maxHosts + " max connections: " + maxConnections+" Sent message pool size: " + topology.SentMessagePoolSize+ " ReactorMax rec: " + gConfig.ReactorMaximumReceivedMessages+
            " Max send pool size: "+ topology.SentMessagePoolSize);
            */


       
    }

    public string GetStats()
    {

        string s, b, z;

#if UNITY_WEBGL
        s = "(webgl doesn't support all stats)";
        b = "";
        z = "";
#else


        /*

          
         byte error;

//        These give the error:  the function called has not been supported for web sockets communication
        
int webQueueIn = NetworkTransport.GetIncomingMessageQueueSize(RTNetworkServer.Get().GetWebHostID(), out error);
        if ((NetworkError)error != NetworkError.Ok)
        {
            RTConsole.Log("NetworkTransport.GetIncomingMessageQueueSize - Error " + (NetworkError)error);
        }

        int webQueueOut = NetworkTransport.GetOutgoingMessageQueueSize(RTNetworkServer.Get().GetWebHostID(), out error);
        if ((NetworkError)error != NetworkError.Ok)
        {
            RTConsole.Log("NetworkTransport.GetOutgoingMessageQueueSize - Error " + (NetworkError)error);
        }

         int webBytesOut = NetworkTransport.GetOutgoingFullBytesCountForHost(RTNetworkServer.Get().GetWebHostID(), out error);
        if ((NetworkError)error != NetworkError.Ok)
        {
            RTConsole.Log("GetOutgoingFullBytesCountForHost - Error " + (NetworkError)error);
        }
    */

        int userPayloadBytes = NetworkTransport.GetOutgoingUserBytesCount();
        int totalBytesSent = NetworkTransport.GetOutgoingFullBytesCount();
        int systemBytesSent = NetworkTransport.GetOutgoingSystemBytesCount();
        int UNETOverhead = systemBytesSent-userPayloadBytes; //This seems to be right.  GetOutgoingSystemBytesCount() also includes the user paypload, so let's get rid of that

        float overhead;
        if (totalBytesSent != 0)
        {
            overhead = (float)UNETOverhead / (float)totalBytesSent;
        }
        else
        {
            overhead = 0;
        }

        s = "Network timestamp: " + NetworkTransport.GetNetworkTimestamp()+"\r\n"; //+".  Compiled "+RTBuildInfo.Timestamp+"\r\n";
        s += "WebGL: Unity doesn't give stats for WebGL, so this is all UDP only below";
       
        b = "";
        /*
        //breaks webgl build.. why isn't this being excluded by the #if properly!?
        b = "UDP: In Msg Queue: " + NetworkTransport.GetIncomingMessageQueueSize(RTNetworkServer.Get().GetHostID(), out error)
            + " Out queue: " + NetworkTransport.GetOutgoingMessageQueueSize(RTNetworkServer.Get().GetHostID(), out error);
     */

        z = "Total packets sent: " + NetworkTransport.GetOutgoingPacketCount() + " Total bytes sent: " + NetworkTransport.GetOutgoingFullBytesCount()
            + " (" + ((totalBytesSent / 1024.0f) / 1024.0f).ToString("0.#") + " MB)" +
            " Unity packet overhead: " + (overhead * 100).ToString("0.##") + "%. (" + UNETOverhead + " bytes) Payload bytes: " + userPayloadBytes + " System (system+payload?): " + systemBytesSent
        + " Packets rec: "+NetworkTransport.GetIncomingPacketCountForAllHosts();

        z += "\r\nPackets dropped: " + NetworkTransport.GetIncomingPacketDropCountForAllHosts();
            

#endif


        return s + "\r\n" + b + "\r\n" + z;

    }


    //These methods below allow both the server OR a client to send messages.  The server sends something that supports the
    //IRTNetworkConnection interface (for example, the RTClientConnection class which the server tracks in a dictionary)
    //Confusingly, the client sends messages by sending ITSELF. (it will actually route to the server)


    public void SendString(IRTNetworkConnection target, string message)
    {
        byte error;
        
        int index = 0;
        short packetType = (short)PacketType.STRING;
        RTUtil.SerializeInt32(message.Length+4, _buffer, ref index); //the +4 is because the string serialization adds the size too
        RTUtil.SerializeInt16(packetType, _buffer, ref index);
        RTUtil.SerializeString(message, _buffer, ref index);

        NetworkTransport.Send(target.GetHostID(), target.GetConnectionID(), myReliableChannelId, _buffer, index, out error);

        if ((NetworkError)error != NetworkError.Ok)
        {
            RTConsole.Log("SendString: Error sending to host " + target.GetHostID() + " ConID: "+target.GetConnectionID()+" : - Error " + (NetworkError)error);
        }
    }

   
    public void SendDB(IRTNetworkConnection target, RTDB db)
    {
        byte error;
        int bytesWritten = 0;
        db.Serialize(RTUtil.g_buffer, (short)PacketType.DB, ref bytesWritten);
        NetworkTransport.Send(target.GetHostID(), target.GetConnectionID(), myReliableChannelId, RTUtil.g_buffer, bytesWritten, out error);

        if ((NetworkError)error != NetworkError.Ok)
        {
            RTConsole.Log("SendDB: Error sending to host " + target.GetHostID() + " ConID: " + target.GetConnectionID() + " : - Error " + (NetworkError)error);
        }
    }

    public void SendPacketNoData(IRTNetworkConnection target, short packetType)
    {
        byte error;
        int index = 0;
        RTUtil.SerializeInt32(0, _buffer, ref index); //we don't include the size + packettype bytes, so zero
        RTUtil.SerializeInt16(packetType, _buffer, ref index);
        NetworkTransport.Send(target.GetHostID(), target.GetConnectionID(), myReliableChannelId, _buffer, index, out error);

        if ((NetworkError)error != NetworkError.Ok)
        {
            RTConsole.Log("SendPacketNoData: Error sending to host " + target.GetHostID() + " ConID: " + target.GetConnectionID() + " : - Error " + (NetworkError)error);
        }
    }

    /*
    public void SendPacketNoData(int tempHostID, int tempConnectionId, short packetType)
    {
        byte error;
        int index = 0;
        RTUtil.SerializeInt32(0, _buffer, ref index); //we don't include the size + packettype bytes, so zero
        RTUtil.SerializeInt16(packetType, _buffer, ref index);
        NetworkTransport.Send(tempHostID, tempConnectionId, myReliableChannelId, _buffer, index, out error);

        if ((NetworkError)error != NetworkError.Ok)
        {
            RTConsole.Log("SendPacketNoData: Error sending to host " + tempHostID + " ConID: " + tempConnectionId + " : - Error " + (NetworkError)error);
        }
    }
    */

    public void CreateDataPacketStart(short packetType, out byte[]  data, out int index)
    {
        index = 4; //skip the size, we'll get to that later, we don't know the size yet
        //RTUtil.SerializeInt32(6, _buffer, ref index);
        RTUtil.SerializeInt16(packetType, _buffer, ref index);
        data = _buffer;
        //wait for the user to finish filling this out
    }

    public void CreateDataPacketEnd(int index)
    {
        _builtPacketSize = index;
        int temp = 0;
        RTUtil.SerializeInt32(index-6, _buffer, ref temp);
       //packet is ready to be sent
    }

    public void SendBuiltRawPacket(IRTNetworkConnection target)
    {
        byte error;
        NetworkTransport.Send(target.GetHostID(), target.GetConnectionID(), myReliableChannelId, _buffer, _builtPacketSize, out error);

        if ((NetworkError)error != NetworkError.Ok)
        {
            RTConsole.Log("SendBuiltRawPacket: Error sending to host " + target.GetHostID() + " ConID: " + target.GetConnectionID() + " : - Error " + (NetworkError)error);
        }
    }

    public void SendBuiltRawPacketCustom(IRTNetworkConnection target, byte[] data, int dataSize)
    {
        byte error;
        NetworkTransport.Send(target.GetHostID(), target.GetConnectionID(), myReliableChannelId, data, dataSize, out error);

        if ((NetworkError)error != NetworkError.Ok)
        {
            RTConsole.Log("SendBuiltRawPacketCustom: Error sending to host " + target.GetHostID() + " ConID: " + target.GetConnectionID() + " : - Error " + (NetworkError)error);
        }
    }
  

    public void SendData(IRTNetworkConnection target, short packetType, byte[] data, int dataSize)
    {
        byte error;
        int index = 0;
        RTUtil.SerializeInt32(6+dataSize, _buffer, ref index);
        RTUtil.SerializeInt16(packetType, _buffer, ref index);
        System.Buffer.BlockCopy(data, 0, _buffer, 6, dataSize); //raw copy over 4 bytes
        NetworkTransport.Send(target.GetHostID(), target.GetConnectionID(), myReliableChannelId, _buffer, 6 + dataSize, out error);

        if ((NetworkError)error != NetworkError.Ok)
        {
            RTConsole.Log("SendData: Error sending to host " + target.GetHostID() + " ConID: " + target.GetConnectionID() + " : - Error " + (NetworkError)error);
        }
    }

}
