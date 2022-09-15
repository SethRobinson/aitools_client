#define LOCALSERVER

using UnityEngine;
using System.Collections;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections.Generic;


public class RTSocketEvent
{
	public RTDB m_db = null;
	public byte[] m_raw = null;
}

public class RTSocket 
{

#if !UNITY_WEBGL
	public enum eErrorCode
	{
		NONE,
		FAILED_TO_CONNECT
	};

    public Action<string> OnTelnetStringReceivedEvent;

    public const short PACKET_TYPE_UNKNOWN = 0;
	public const short PACKET_TYPE_DB = 1;
	public const short PACKET_TYPE_MOVE = 2;


    private TcpClient _tcpClient = null;
	Queue<RTSocketEvent> m_events;
	byte[] m_mainBuf = new byte[1024*100]; //this determines the max RT packet size we could handle btw..
	byte[] m_tempBuf = new byte[1024*100]; //this determines the max RT packet size we could handle btw..
    int m_buffPos = 0;
	eErrorCode m_errorCode = eErrorCode.NONE;
    string _ipAddress;

	string m_errorString;

    public TcpClient GetTcpClient() { return _tcpClient; }

    public int GetEventCount()
	{
		return m_events.Count;
	}

    public string GetIP() { return _ipAddress; }

	public void ResetErrors()
	{
		m_errorCode = eErrorCode.NONE;
		m_errorString = "";
	}
	public string GetErrorString()
	{
		return m_errorString;
	}

	public eErrorCode GetErrorCode()
	{
		return m_errorCode;
	}

	void SetError(eErrorCode code, string errorMsg)
	{
		m_errorCode = code;
		m_errorString = errorMsg;
	}

	public void SocketKill()
	{
		ResetErrors();
		if (_tcpClient != null && _tcpClient.Connected)
		{

//            Debug.Log("Closing socket");

            NetworkStream networkStream;
			
			try
			{
				networkStream = _tcpClient.GetStream();   
			}
			
			catch(Exception e)
			{
                Debug.Log("Woah, error reading from socket "+e.ToString());
                return;
            }   

			networkStream.Close();
			_tcpClient.Close();
            _ipAddress = "";
		}

        _tcpClient = null;

    }

    public void SetSocket(TcpClient newSocket)
    {

        SocketKill();
        ResetErrors();

        _tcpClient = newSocket;
        _tcpClient.NoDelay = true;
        
        try
        {
            //We are connected successfully.
            //Debug.Log("Receive Buffer size: "+tcpClient.ReceiveBufferSize);

            NetworkStream networkStream;

            try
            {
                networkStream = _tcpClient.GetStream();
            }

            catch (Exception e)
            {
                if (e == null)
                {
                    //just doing something to get rid of the declared but not used issue
                }
                SetError(eErrorCode.FAILED_TO_CONNECT, "Failed to connect, server may be down. ");
                //SetError(eErrorCode.FAILED_TO_CONNECT, "Failed to connect, server may be down. "+e);

                return;
            }

            byte[] buffer = new byte[_tcpClient.ReceiveBufferSize];

            //and write some stuff too

            //Now we are connected start asyn read operation.
            _ipAddress = IPAddress.Parse(((IPEndPoint)_tcpClient.Client.RemoteEndPoint).Address.ToString()).ToString();
           
            networkStream.BeginRead(buffer, 0, buffer.Length, ReadCallback, buffer);
        }
        catch (Exception ex)
        {
            Debug.Log("SetSocket: "+ ex.Message);
        }
    }


    public bool Connect(string host, int port)
	{
		ResetErrors();

		m_events = new Queue<RTSocketEvent>();
		m_buffPos = 0;

#if UNITY_WEBPLAYER
		//Unneeded, unity does it by itself as long as we're on a standard port
		//Security.PrefetchSocketPolicy(host, 843)
#else
#endif

		try
		{
			_tcpClient = new TcpClient(AddressFamily.InterNetwork);
			IPAddress[] remoteHost = Dns.GetHostAddresses(host);
			_tcpClient.NoDelay = true;
			_tcpClient.BeginConnect(remoteHost, port, new
		                       AsyncCallback(ConnectCallback), _tcpClient);
		}
		catch (Exception ex)
		{
			Debug.Log("RTSocket connect: "+ex.Message);
     	}

		return true; //success
	 }

	
	public void WriteString(string s)
	{
	
		if (_tcpClient == null || !_tcpClient.Connected)
		{
			Debug.Log ("Can't send string, not connected to socket");
			return;
		}

 	    System.IO.StreamWriter writer = new System.IO.StreamWriter(_tcpClient.GetStream());
        writer.Write(s);
        writer.Flush();
    }

	public void WriteBuffer(int offset, int size,  byte[] byteBuff = null)
	{
	
		if (_tcpClient == null || !_tcpClient.Connected)
		{
			Debug.Log ("Can't send string, not connected to socket");
			return;
		}
		
		NetworkStream networkStream;
		
		try
		{
			networkStream = _tcpClient.GetStream();   
		}
		
		catch(Exception e)
		{
			Debug.Log("WriteBuffer>Error reading from socket " + e.ToString());
			return;
		}   

		if (byteBuff == null) byteBuff = RTUtil.g_buffer;

		networkStream.Write(byteBuff, offset, size);
	}

	public void WritePacket(RTDB packet)
	{
		if (_tcpClient == null || !_tcpClient.Connected)
		{
			Debug.Log ("Can't send string, not connected to socket");
			return;
		}
		
		NetworkStream networkStream;
		
		try
		{
			networkStream = _tcpClient.GetStream();   
		}
		
		catch(Exception e)
		{
			Debug.Log("Woah, error reading from socket "+e.ToString());
            return;
        }   
        
        //	Debug.Log(packet.ToString());
		int size = 0;
        //note: I made some changes here to RTDB's serialization code and never tested them, so this
        //could be broken...
		packet.Serialize(RTUtil.g_buffer, PACKET_TYPE_DB, ref size);

		networkStream.Write(RTUtil.g_buffer, 0, size);
	}

	public bool IsConnected()
	{ 
		if (_tcpClient == null) return false;
		
		return _tcpClient.Connected;
	}

	public RTSocketEvent PopNextEvent()
	{
		if (m_events.Count == 0) return null;
		return m_events.Dequeue();
	}

    private string ApplyBackSpaces(string s)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder("");
       
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == 8 && i < s.Length)
            {
                //found backspace
                if (sb.Length > 1)
                    sb.Remove(sb.Length-1, 1);
                //i++; //skip CR too
                continue;
            }
            sb.Append(s[i]);
        }

        return sb.ToString();
    }

	private void ConnectCallback(IAsyncResult result)
	{          

		if (_tcpClient == null) 
		{
			Debug.Log("Can't connectcallback, not connected");
			return;
		}

		try
		{
			//We are connected successfully.
			//Debug.Log("Receive Buffer size: "+tcpClient.ReceiveBufferSize);

			NetworkStream networkStream;

			try
			{
				networkStream = _tcpClient.GetStream();   
			}
			
			catch(Exception e)
			{
				if (e == null)
				{
					//just doing something to get rid of the declared but not used issue
				}
				SetError(eErrorCode.FAILED_TO_CONNECT, "Failed to connect, server may be down. ");
				//SetError(eErrorCode.FAILED_TO_CONNECT, "Failed to connect, server may be down. "+e);

				return;
			}    

			byte[] buffer = new byte[_tcpClient.ReceiveBufferSize];

			//and write some stuff too

			//Now we are connected start asyn read operation.
			networkStream.BeginRead(buffer, 0, buffer.Length, ReadCallback, buffer);
		}
		catch(Exception ex)
		{
			Debug.Log("ConnectCallback: "+ex.Message);
        }

     }
           
          /// Callback for Read operation
	private void ReadCallback(IAsyncResult result)
    {     
            NetworkStream networkStream;
            try
            {
                networkStream = _tcpClient.GetStream();   
            }

            catch
            {
			Debug.Log("Error reading from socket");
			return;
			}         

		int bytesRead = networkStream.EndRead (result);

		//Debug.Log("Got "+bytesRead+" data in a thread..");

		if (bytesRead == 0)
		{
			//Debug.Log ("That's weird, nothing came in .. wait I guess");
			return;
		}

		byte[] buffer = result.AsyncState as byte[];

		if (bytesRead+m_buffPos > m_mainBuf.Length)
		{
			Debug.Log ("Huge error, buffer is too small for this packet");
			return;
		}
        bool doLocalEcho = false;

        //if (OnTelnetStringReceivedEvent != null) doLocalEcho = true;

        string echoLater = null;

        if (doLocalEcho)
        {
           echoLater = System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead);
           
        }

        //move EVERYTHING to our own buffer
        System.Buffer.BlockCopy(buffer, 0, m_mainBuf, m_buffPos, bytesRead);
		m_buffPos += bytesRead;

        if (OnTelnetStringReceivedEvent != null) //should we go into vanilla text "telnet" processing mode?
        {
            //simple mode, just fine strings after a CR

            while (m_buffPos > 0)
            {
                //did a whole string get here yet? (we'll look for the /n)
                for (int i=0; i < m_buffPos; i++)
                {
                    if (m_mainBuf[i] == 13 || m_mainBuf[i] == 10)
                    {
                        //found CR, this is a line I guess.  Cut it out

                        string s = System.Text.Encoding.UTF8.GetString(m_mainBuf, 0, i);
                        //Debug.Log("Got " + s);

                        //well, it's a bother for the receiving guy to worry about thread safety to do this now but...

                        //apply backspaces we find...
                        s = ApplyBackSpaces(s);

                        OnTelnetStringReceivedEvent(s); //send it out

                        //add one, we don't want the CR
                        i++;

                        if (m_mainBuf[i] == 10 || m_mainBuf[i] == 13)
                        {
                            //kill that too, it's a \r
                            i++;
                        }

                        //cut out the part we just processed.  We're copying a chunk over itself.. is this legit??
                        System.Buffer.BlockCopy(m_mainBuf, i, m_tempBuf, 0, m_buffPos - i);
                        //now move it back
                        System.Buffer.BlockCopy(m_tempBuf, 0, m_mainBuf, 0, m_buffPos - i);

                        //fix offset
                        m_buffPos = m_buffPos - i;
                        continue;
                    }
                }

                //guess we're done here for now
                //Debug.Log("We only have " + m_buffPos + " bytes... waiting");
               
                break;

            }

        } else
        {

            //complex packet process mode

		    //take a look at our own buff and see if it's ready to do anything with

		    while (m_buffPos > 5)
		    {
			    //did the full packet get here yet?
			    int packetSize = 0;

			    int p = 0;
			    //read
			    RTUtil.SerializeInt32(ref packetSize, m_mainBuf, ref p);

			    //we now have enough to grab a full packet

			    if (m_buffPos >= packetSize) 
			    {
				    //ok, we have enough data
				   // Debug.Log ("Packet is "+packetSize+" -  total buff is "+m_buffPos);

				    //figure out if it's a raw data or a rtdb packet
				    short packetType =0;

				    RTUtil.SerializeInt16(ref packetType, m_mainBuf, ref p);

				    if (packetType == PACKET_TYPE_DB)
				    {
					    RTSocketEvent e = new RTSocketEvent();
					    e.m_db = new RTDB();
                        int tempIndex = 0;

                        e.m_db.DeSerialize(m_mainBuf, ref tempIndex);
					    //Debug.Log ("Got: "+e.m_db.ToString());
					    m_events.Enqueue(e);
				    } else
				    {
					    //it's raw data
					    RTSocketEvent e = new RTSocketEvent();
					    e.m_raw = new byte[packetSize];
					    //actually copy the data
					    System.Buffer.BlockCopy(m_mainBuf, 0, e.m_raw, 0, packetSize);
					    m_events.Enqueue(e);
				    }

				    //cut out the part we just processed.  We're copying a chunk over itself.. is this legit??
				    System.Buffer.BlockCopy(m_mainBuf, packetSize, m_tempBuf, 0, m_buffPos-packetSize);
				    //now move it back
				    System.Buffer.BlockCopy(m_tempBuf, 0, m_mainBuf, 0, m_buffPos-packetSize);

				    //fix offset
				    m_buffPos = m_buffPos-packetSize;
			    } else
			    {
				    //Debug.Log ("We only have "+m_buffPos+" bytes, but we need "+packetSize+" so we'll wait");
			    }
				
		    }

        }

        //need to wait for more data
        networkStream.BeginRead(buffer, 0, buffer.Length, ReadCallback, buffer);

        if (echoLater != null && echoLater.Length > 0)
            WriteString(echoLater);
    }
#endif
}
