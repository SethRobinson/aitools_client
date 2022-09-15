using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

public class MyRef<T>
{
    public T _ref { get; set; }
}


public class RTDB 
{

	//these match Proton's variant types.. entity and component are unused here in unity though
	public const int TYPE_UNUSED=0;
	public const int TYPE_FLOAT=1;
	public const int TYPE_STRING=2;
	public const int TYPE_VECTOR2=3;
	public const int TYPE_VECTOR3=4;
	public const int TYPE_UINT32=5;
	public const int TYPE_ENTITY=6;
	public const int TYPE_COMPONENT=7;
	public const int TYPE_RECT=8;
	public const int TYPE_INT32=9;
    public const int TYPE_INT16 = 10;
    public const int TYPE_VECTOR2INT = 11;
    public const int TYPE_BOOL = 12;
	public const int TYPE_QUATERNION = 13;


	Dictionary<string, object> m_database = new Dictionary<string, object>();

	public RTDB()
	{
	}

    public Dictionary<string, object> GetDictionary() { return m_database; }

    public void Clear()
	{
		m_database.Clear();
	}
	public RTDB(string key1, object value1)
	{
		m_database[key1] = value1;
	}
	
	public RTDB(string key1, object value1, string key2, object value2)
	{
		m_database[key1] = value1;
		m_database[key2] = value2;
	}

	public RTDB(string key1, object value1, string key2, object value2, string key3, object value3)
	{
		m_database[key1] = value1;
		m_database[key2] = value2;
		m_database[key3] = value3;
	}

	public RTDB(string key1, object value1, string key2, object value2, string key3, object value3, string key4, object value4)
	{
		m_database[key1] = value1;
		m_database[key2] = value2;
		m_database[key3] = value3;
		m_database[key4] = value4;
	}

    public RTDB(string key1, object value1, string key2, object value2, string key3, object value3, string key4, object value4, string key5, object value5)
	{
		m_database[key1] = value1;
		m_database[key2] = value2;
		m_database[key3] = value3;
		m_database[key4] = value4;
        m_database[key5] = value5;
    }
	
	public object Get(string key)
	{
		return m_database[key];	
	}

    public bool ContainsKey(string key)
    {
        return m_database.ContainsKey(key);
    }

    public int GetInt32(string key)
	{
		return (int)m_database[key];	
	}

    public bool GetBool(string key)
    {
        return (bool)m_database[key];
    }

    public short GetInt16(string key)
    {
        return (short)m_database[key];
    }

    public float GetFloat(string key)
    {
        return (float)m_database[key];
    }

    public uint GetUInt32(string key)
    {
        return (uint)m_database[key];
    }

    public string GetString(string key)
	{
		if (m_database[key].GetType() != typeof(string))
		{
			Debug.LogWarning(key+" should be string but is "+m_database[key].GetType());
		}
		return m_database[key] as string;	
	}
	
	public Vector3 GetVector3(string key)
	{
		if (m_database[key].GetType() != typeof(Vector3))
		{
			Debug.LogWarning(key+" should be vector3 but is "+m_database[key].GetType());
		}
		return (Vector3)m_database[key];	
	}

	public Quaternion GetQuaternion(string key)
	{
		if (m_database[key].GetType() != typeof(Quaternion))
		{
			Debug.LogWarning(key + " should be Quaternion but is " + m_database[key].GetType());
		}
		return (Quaternion)m_database[key];
	}

	public Vector2 GetVector2(string key)
    {
        if (m_database[key].GetType() != typeof(Vector2))
        {
            Debug.LogWarning(key + " should be vector2 but is " + m_database[key].GetType());
        }
        return (Vector2)m_database[key];
    }

    public Vector2Int GetVector2Int(string key)
    {
        if (m_database[key].GetType() != typeof(Vector2Int))
        {
            Debug.LogWarning(key + " should be vector2 but is " + m_database[key].GetType());
        }
        return (Vector2Int)m_database[key];
    }

    public float GetFloatWithDefault(string key, float v)
	{
		if (m_database.ContainsKey(key))
		{
			return (float)m_database[key];
		}
		
		//create it and set the default
		
		m_database[key] = v;
		return v;
	}

    public string GetStringWithDefault(string key, string v)
    {
        if (m_database.ContainsKey(key))
        {
            if (m_database[key].GetType() != typeof(string))
            {
                Debug.LogWarning(key + " should be string but is " + m_database[key].GetType());
            }

            return m_database[key] as string;
        }

        //set a dault
        m_database[key] = v;
        return v;
    }

    public bool GetBoolWithDefault(string key, bool v)
	{
		if (m_database.ContainsKey(key))
		{
			return (bool)m_database[key];
		}
		
		//create it and set the default
		
		m_database[key] = v;
		return v;
	}
	
	public void Set(string key, object v)
	{
		m_database[key] = v;	
	}
	
	public bool RemoveKey(string key) //returns true if key was removed
    {
        return m_database.Remove(key);
        
    }

    public override string ToString()
	{
		string s = "";
		s += "RTDB contains "+m_database.Count+" pairs: ";
		
		foreach (KeyValuePair<string, object> pair in m_database)
		{
		    s += pair.Key + "=" + pair.Value+" ("+pair.Value.GetType()+"), ";
		}	
		
		return s;
	}

	public void Serialize(byte[] buffer, short packetType, ref int index)
	{

        int rememberStartingByte = index;

		index += 4; //skip size, we'll come back
	
		//writing to mem
		RTUtil.SerializeInt16(packetType, buffer, ref index);
		RTUtil.SerializeInt16((short)m_database.Count, buffer, ref index);
	
		//now let's add each kind of thing we want to serialize
		foreach (KeyValuePair<string, object> pair in m_database)
		{

			RTUtil.SerializeString(pair.Key as string, buffer, ref index);

			if (pair.Value.GetType() == typeof(string))
			{
				buffer[index] = (byte)TYPE_STRING; index += 1;
				RTUtil.SerializeString(pair.Value as string, buffer, ref index);
			} else if (pair.Value.GetType() == typeof(int))
			{
				buffer[index] = (byte)TYPE_INT32; index += 1;
				RTUtil.SerializeInt32((int)pair.Value, buffer, ref index);
            }  else if (pair.Value.GetType() == typeof(uint))
            {
                buffer[index] = (byte)TYPE_UINT32; index += 1;
                RTUtil.SerializeUInt32((uint)pair.Value, buffer, ref index);
            }
            else if (pair.Value.GetType() == typeof(bool))
            {
                buffer[index] = (byte)TYPE_BOOL; index += 1;
                RTUtil.SerializeBool((bool)pair.Value, buffer, ref index);
            }
            else if (pair.Value.GetType() == typeof(float))
			{
				buffer[index] = (byte)TYPE_FLOAT; index += 1;
				RTUtil.SerializeFloat((float)pair.Value, buffer, ref index);
            } else if (pair.Value.GetType() == typeof(short))
            {
                buffer[index] = (byte)TYPE_INT16; index += 1;
                RTUtil.SerializeInt16((short)pair.Value, buffer, ref index);
            }
            else if (pair.Value.GetType() == typeof(Vector2))
			{
				buffer[index] = (byte)TYPE_VECTOR2; index += 1;
				RTUtil.SerializeVector2((Vector2)pair.Value, buffer, ref index);
            }
			else if (pair.Value.GetType() == typeof(Vector3))
			{
				buffer[index] = (byte)TYPE_VECTOR3; index += 1;
				RTUtil.SerializeVector3((Vector3)pair.Value, buffer, ref index);
			}
			else if (pair.Value.GetType() == typeof(Quaternion))
			{
				buffer[index] = (byte)TYPE_QUATERNION; index += 1;
				RTUtil.SerializeQuaternion((Quaternion)pair.Value, buffer, ref index);
			}
			else if (pair.Value.GetType() == typeof(Vector2Int))
            {
                buffer[index] = (byte)TYPE_VECTOR2INT; index += 1;
                RTUtil.SerializeVector2Int((Vector2Int)pair.Value, buffer, ref index);
            }
            else
            {
				Debug.LogWarning(pair.Key+" can't be serialized, we don't know how to have a "+pair.Value.GetType());
			}
		}	
		//fill in total size now that we know.. writing to index 0 instead of p for this

	   //we don't include the first 6 bytes when calculating the total content length
		RTUtil.SerializeInt32(index-6, buffer,ref rememberStartingByte); //force index 0, and ignore what it returns, we don't care
		
	}

    //if a class var exactly matches a db key and the type also matches, we'll set the var
    public void SetClassVarsFromDBViaReflection(object obj)
    {

        BindingFlags bindingFlags = BindingFlags.Public |
                         BindingFlags.NonPublic |
                         BindingFlags.Instance;

        foreach (KeyValuePair<string, object> pair in m_database)
        {
            FieldInfo field = obj.GetType().GetField(pair.Key, bindingFlags);

            if (field == null)
            {
                Debug.Log("SetClassVarsFromDBViaReflection unable to find class var named " + pair.Key + ", ignoring");
                continue;
            }
            if (field.FieldType == pair.Value.GetType())
            {
                Debug.Log("SetClassVarsFromDBViaReflection set" + pair.Key + " value");
                field.SetValue(obj, pair.Value);
            } else
            {
                Debug.Log("SetClassVarsFromDBViaReflection ignoring " +pair.Key+", wrong field type");
            }
        }
    }

        public void PopulateDBFromClassVarsViaReflection(object obj, string variablesStartingWith)
    {
        //RTConsole.Log("Scanning " + obj.GetType().Name);

        BindingFlags bindingFlags = BindingFlags.Public |
                            BindingFlags.NonPublic |
                            BindingFlags.Instance;
                           //| BindingFlags.Static;

        foreach (FieldInfo field in obj.GetType().GetFields(bindingFlags))
        {

          //  RTConsole.Log("Found: " + field.Name + " is type " + field.FieldType + " and the value is " + field.GetValue(obj).ToString());

            if (field.Name.StartsWith(variablesStartingWith))
            {
              //  RTConsole.Log("Populating: "+field.Name + " is type " + field.FieldType + " and the value is " + field.GetValue(obj).ToString());
                Set(field.Name, field.GetValue(obj));
            }
        }
      //  RTConsole.Log(this.ToString());
        
    }

    public bool DeSerialize(byte[] packet, ref int index)
	{
	
		Clear();

		//read from mem
		
        int totalBytes = 0;
        RTUtil.SerializeInt32(ref totalBytes, packet, ref index);

        short packetType = 0;
		RTUtil.SerializeInt16(ref packetType, packet, ref index);

		short itemCount = 0;
		RTUtil.SerializeInt16(ref itemCount, packet, ref index);


		//grab all items and add as new items

		for (int i=0; i < itemCount; i++)
		{
			string key = "";
			if (!RTUtil.SerializeString(ref key, packet, ref index))
            {
                //an error has occurred
                Debug.Log("Error reading string in RTDB, aborting");
                return false;
            }
			int type = (int)packet[index]; index += 1; 
		
			switch (type)
			{
			case TYPE_STRING:
			{
				string value = "";
				RTUtil.SerializeString(ref value, packet, ref index);

				//add it
				Set(key, value);
			}
				break;

			case TYPE_INT32:
			{
				int value = 0;
				RTUtil.SerializeInt32(ref value, packet, ref index);
				
				//add it
				Set(key, value);
                    }
                    break;

                case TYPE_BOOL:
                    {
                        bool value = false;
                        RTUtil.SerializeBool(ref value, packet, ref index);

                        //add it
                        Set(key, value);
                    }
                    break;

                case TYPE_UINT32:
                    {
                        uint value = 0;
                        RTUtil.SerializeUInt32(ref value, packet, ref index);

                        //add it
                        Set(key, value);
                    }
                    break;

                case TYPE_INT16:
                    {
                        short value = 0;
                        RTUtil.SerializeInt16(ref value, packet, ref index);

                        //add it
                        Set(key, value);
                    }
                    break;

                case TYPE_FLOAT:
                    {
                        float value = 0;
                        RTUtil.SerializeFloat(out value, packet, ref index);
				
				//add it
				Set(key, value);
			}
				break;


			case TYPE_VECTOR2:
			{
				Vector2 vTemp;
				RTUtil.SerializeVector2(out vTemp, packet, ref index);
			
				//add it
				Set(key, vTemp);
			}
				break;

				case TYPE_VECTOR3:
					{
						Vector3 vTemp = new Vector3();
						RTUtil.SerializeVector3(ref vTemp, packet, ref index);

						//add it
						Set(key, vTemp);
					}
					break;
				case TYPE_QUATERNION:
					{
						Quaternion vTemp = new Quaternion();
						RTUtil.SerializeQuaternion(ref vTemp, packet, ref index);

						//add it
						Set(key, vTemp);
					}
					break;
				case TYPE_VECTOR2INT:
                    {
                        Vector2Int vTemp = new Vector2Int(0, 0);
                        RTUtil.SerializeVector2Int(ref vTemp, packet, ref index);

                        //add it
                        Set(key, vTemp);
                    }
                    break;


                default:

				Debug.LogWarning("Error, don't know what type "+type+" is in DeSerialize");
				return false;
			}
		}

        return true; //success

		//Debug.Log ("Decrypted size is "+totalBytes+" and itemcount is "+itemCount);
	}
}
