using System;
using System.Net.Sockets;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace STARS
{
    public class StarsInterface
    {
        //properties
        public string NodeName { set; get; }
        public string ServerHostname { set; get; }
        public int ServerPort { set; get; }
        public string KeyFile { set; get; }
        public string KeyWord { set; get; }
        public decimal DefaultTimeout {
            set {
                defaultTimeout = (int)(value * 1000);
            }
            get {
                return (decimal)defaultTimeout / 1000;
            }
        }
        public bool IsConnected { set; get; } = false;

        //fields
        private int defaultTimeout = 30000;
        private Socket sock;

        //These variables are used for Read messages.
        private byte[] readBuffer;
        private int readCount;
        private int processedCount;
        //private ArrayList readArray;
        private int processedLevel;  //shows progress of message processing (Processed.. 0=Nothing, 1=From, 2=To, 3=Command, 4=Parameter)
        private ArrayList[] mesProcArray; //Buffer for message processing.

        //constructor
        public StarsInterface(string nodeName, string svrHost, string keyFile, int svrPort, decimal timeOut = 30.0m)
        {
            NodeName = nodeName;
            ServerHostname = svrHost;
            ServerPort = svrPort;
            KeyFile = keyFile;
            KeyWord = "";
            DefaultTimeout = timeOut;
            sock = null;
            readBuffer = new byte[1024];
            readCount = 0;
            processedCount = 0;
            //readArray = new ArrayList();
            processedLevel = 0;
            mesProcArray = new ArrayList[4];
            int lp;
            for (lp = 0; lp <= 3; lp++) { mesProcArray[lp] = new ArrayList(); }
        }

        public StarsInterface(string nodeName, string svrHost, string keyFile) : this(nodeName, svrHost, keyFile, 6057) { }

        public StarsInterface(string nodeName, string svrHost) : this(nodeName, svrHost, nodeName + ".key", 6057) { }

        //methods
        public void Connect(bool callbackmode = false)
        {
            IsConnected = false;
            
            //Read keyword
            List<string> keyword;
            try
            {
                keyword = GetKeywords();
            }
            catch (Exception e)
            {
                throw new StarsException("Could not open keyword file.: " + e.Message);
            }

            //Establish TCP/IP Socket
            try
            {
                sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                sock.Connect(ServerHostname, ServerPort);
            }
            catch (Exception e)
            {
                throw new StarsException("Could not establish TCP/IP connection.: " + e.Message);
            }
            sock.Blocking = true;

            //Get random number.
            var rNum = int.Parse(Receive().from);

            //Get keyword and send to STARS server.
            tcpSendString($"{NodeName} {keyword[(rNum % keyword.Count)]}");
            StarsMessage rdBuf = Receive();
            if (rdBuf.command != "Ok:")
            {
                throw new StarsException("Could not connect to server.: " + rdBuf.Message);
            }

            //set event
            if(callbackmode)
            {
                CallbackOn();
            }

            IsConnected = true;
        }

        public bool CallbackOn()
        {
            try
            {
                sock.BeginReceive(readBuffer, 0, readBuffer.Length, SocketFlags.None, new AsyncCallback(ReceivedMessage), null);
            }
            catch (Exception)
            {
                return false;
            }

            return true;
        }

        //Read keyword list from file.
        private List<string> GetKeywords()
        {
            List<string> keyword;
            if (KeyWord != "")
            {
                keyword = new List<string>(KeyWord.Split(' '));
            }
            else
            {
                keyword = new List<string>();
                StreamReader reader = new StreamReader(KeyFile);
                try
                {
                    do
                    {
                        keyword.Add(reader.ReadLine());
                    }
                    while (reader.Peek() != -1);
                }
                finally
                {
                    reader.Close();
                }
            }
            return keyword;
        }


        public void Disconnect()
        {
            sock.Close();
        }

        //STARS Send
        public void Send(string sndFrom, string sndTo, string sndCommand)
        {
            tcpSendString($"{sndFrom} > {sndTo} {sndCommand}");
        }

        public void Send(string sndTo, string sndCommand)
        {
            tcpSendString($"{sndTo} {sndCommand}");
        }

        public void Send(string sndCommand)
        {
            tcpSendString(sndCommand);
        }

        //Send strings TCP/IP socket.
        private void tcpSendString(string sndBuf)
        {
            sock.Send(Encoding.ASCII.GetBytes(sndBuf + "\n"));
        }

        //STARS Receive
        public StarsMessage Receive(int timeout)
        {
            StarsMessage rdMes = ReceiveCommon(timeout);
            return rdMes;
        }

        public StarsMessage Receive()
        {
            return Receive(defaultTimeout);
        }

        private StarsMessage ReceiveCommon(int timeout)
        {
            var rdMes = new StarsMessage();

            while (!ProceessMessage(ref rdMes))
            {
                try
                {
                    sock.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout, timeout);
                    readCount = sock.Receive(readBuffer, SocketFlags.None);
                    processedCount = 0;
                }
                catch (Exception e)
                {
                    //Clear buffers with error.
                    readCount = 0;
                    processedCount = 0;
                    for (int lp = 0; lp < 4; lp++) { mesProcArray[lp].Clear(); }
                    processedLevel = 0;

                    sock.Blocking = true;
                    throw new StarsException($"Receive error.: {e.Message}");
                }

                if (readCount < 1)
                {
                    throw new StarsException($"Could not read.: {readCount.ToString()}");
                }
            }
            return rdMes;
        }

        private bool ProceessMessage(ref StarsMessage rdMess)
        {
            byte[] delimiter = { 0x3e, 0x20, 0x20, 0x0a };
            //rdMess.Clear();
            byte nret;
            int lp;
            while (processedCount < readCount)
            {
                nret = readBuffer[processedCount];
                processedCount++;
                if (nret == 0x0d) { continue; }
                if (nret == 0x0a)
                {
                    rdMess = new StarsMessage(Array2String(mesProcArray[0]), Array2String(mesProcArray[1]), Array2String(mesProcArray[2]), Array2String(mesProcArray[3]));
                    for (lp = 0; lp < 4; lp++) { mesProcArray[lp].Clear(); }
                    processedLevel = 0;
                    return true;
                }
                if (nret == delimiter[processedLevel]) { processedLevel++; continue; }
                mesProcArray[processedLevel].Add(nret);
            }
            processedCount = 0;
            readCount = 0;
            return false;
        }

        private string Array2String(ArrayList al)
        {
            byte[] bBuf = new byte[al.Count];
            for (int i = 0; i < al.Count; i++)
            {
                bBuf[i] = (byte)al[i];
            }
            return Encoding.Default.GetString(bBuf, 0, al.Count);
        }

        //Callback
        private StarsMessage cbMessage = new StarsMessage();

        private void ReceivedMessage(IAsyncResult asyncResult)
        {
            try
            {
                readCount = sock.EndReceive(asyncResult);
            }
            catch
            {
                return;
            }

            if (readCount <= 0)
            {
                return;
            }

            processedCount = 0;

            while (ProceessMessage(ref cbMessage))
            {
                OnDataReceived(new StarsCbArgs(cbMessage.from, cbMessage.to, cbMessage.command, cbMessage.parameters));
            }
            sock.BeginReceive(readBuffer, 0, readBuffer.Length, SocketFlags.None, new AsyncCallback(ReceivedMessage), null);
        }

        public event EventHandler<StarsCbArgs> DataReceived;
        protected virtual void OnDataReceived(StarsCbArgs e)
        {
            DataReceived?.Invoke(this, e);
        }

        //This is Finalize
        ~StarsInterface()
        {
            if (sock != null)
            {
                Disconnect();
            }
        }

    }

    public class StarsMessage
    {
        public string from { private set; get; } = "";
        public string to { private set; get; } = "";
        public string command { private set; get; } = "";
        public string parameters { private set; get; } = "";

        //constructor
        public StarsMessage(string from, string to, string command, string parameters)
        {
            this.from = from;
            this.to = to;
            this.command = command;
            this.parameters = parameters;
        }

        public StarsMessage() : this("", "", "", "") { }

        /// <summary>Used to indicate the whole Stars Message.</summary>
        public string allMessage {
            get {
                if (parameters.Length == 0)
                {
                    return $"{from}>{to} {command}";
                }
                else
                {
                    return $"{from}>{to} {command} {parameters}";
                }
            }
        }

        /// <summary>Used to indicate the 'Message', the element of Stars Message.</summary>
        public string Message {
            get {
                if (parameters.Length == 0)
                {
                    return command;
                }
                else
                {
                    return $"{command} {parameters}";
                }
            }
        }

        /// <summary>Returns strings array from parameters.</summary>
        public string[] ParamStrArray(char spc)
        {
            return parameters.Split(spc);
        }

        public short[] ParamShortArray(char spc)
        {
            return StarsConvParams.ToShortArray(parameters, spc);
        }

        public ushort[] ParamUShortArray(char spc)
        {
            return StarsConvParams.ToUShortArray(parameters, spc);
        }

        public int[] ParamIntArray(char spc)
        {
            return StarsConvParams.ToIntArray(parameters, spc);
        }

        public uint[] ParamUIntArray(char spc)
        {
            return StarsConvParams.ToUIntArray(parameters, spc);
        }

        public long[] ParamLongArray(char spc)
        {
            return StarsConvParams.ToLongArray(parameters, spc);
        }

        public ulong[] ParamULongArray(char spc)
        {
            return StarsConvParams.ToULongArray(parameters, spc);
        }

        public float[] ParamFloatArray(char spc)
        {
            return StarsConvParams.ToFloatArray(parameters, spc);
        }

        public double[] ParamDoubleArray(char spc)
        {
            return StarsConvParams.ToDoubleArray(parameters, spc);
        }

        public decimal[] ParamDecimalArray(char spc)
        {
            return StarsConvParams.ToDecimalArray(parameters, spc);
        }

        public bool[] ParamBoolArray(char spc)
        {
            return StarsConvParams.ToBoolArray(parameters, spc);
        }

    }

    public class StarsCbArgs : EventArgs
    {
        public StarsMessage STARS;

        public StarsCbArgs(string from, string to, string command, string parameters)
        {
            STARS = new StarsMessage(from, to, command, parameters);
        }

    }

    public class StarsException : ApplicationException
    {
        public StarsException(string message) : base(message)
        {
        }
    }

    public static class StarsConvParams
    {
        //ToShort ==========================================
        public static short[] ToShortArray(string str, char sp)
        {
            string[] strArray = str.Split(sp);
            int l = strArray.Length;
            short[] vArray = new short[l];
            int lp;
            try
            {
                for (lp = 0; lp < l; lp++)
                {
                    vArray[lp] = System.Convert.ToInt16(strArray[lp]);
                }
            }
            catch
            {
                return new short[0];
            }
            return vArray;
        }

        //ToUShort ==========================================
        public static ushort[] ToUShortArray(string str, char sp)
        {
            string[] strArray = str.Split(sp);
            int l = strArray.Length;
            ushort[] vArray = new ushort[l];
            int lp;
            try
            {
                for (lp = 0; lp < l; lp++)
                {
                    vArray[lp] = System.Convert.ToUInt16(strArray[lp]);
                }
            }
            catch
            {
                return new ushort[0];
            }
            return vArray;
        }

        //ToInt ==========================================
        public static int[] ToIntArray(string str, char sp)
        {
            string[] strArray = str.Split(sp);
            int l = strArray.Length;
            int[] vArray = new int[l];
            int lp;
            try
            {
                for (lp = 0; lp < l; lp++)
                {
                    vArray[lp] = System.Convert.ToInt32(strArray[lp]);
                }
            }
            catch
            {
                return new int[0];
            }
            return vArray;
        }

        //ToUInt ==========================================
        public static uint[] ToUIntArray(string str, char sp)
        {
            string[] strArray = str.Split(sp);
            int l = strArray.Length;
            uint[] vArray = new uint[l];
            int lp;
            try
            {
                for (lp = 0; lp < l; lp++)
                {
                    vArray[lp] = System.Convert.ToUInt32(strArray[lp]);
                }
            }
            catch
            {
                return new uint[0];
            }
            return vArray;
        }

        //ToLong ==========================================
        public static long[] ToLongArray(string str, char sp)
        {
            string[] strArray = str.Split(sp);
            int l = strArray.Length;
            long[] vArray = new long[l];
            int lp;
            try
            {
                for (lp = 0; lp < l; lp++)
                {
                    vArray[lp] = System.Convert.ToInt64(strArray[lp]);
                }
            }
            catch
            {
                return new long[0];
            }
            return vArray;
        }

        //ToULong ==========================================
        public static ulong[] ToULongArray(string str, char sp)
        {
            string[] strArray = str.Split(sp);
            int l = strArray.Length;
            ulong[] vArray = new ulong[l];
            int lp;
            try
            {
                for (lp = 0; lp < l; lp++)
                {
                    vArray[lp] = System.Convert.ToUInt64(strArray[lp]);
                }
            }
            catch
            {
                return new ulong[0];
            }
            return vArray;
        }

        //ToFloat ==========================================
        public static float[] ToFloatArray(string str, char sp)
        {
            string[] strArray = str.Split(sp);
            int l = strArray.Length;
            float[] vArray = new float[l];
            int lp;
            try
            {
                for (lp = 0; lp < l; lp++)
                {
                    vArray[lp] = System.Convert.ToSingle(strArray[lp]);
                }
            }
            catch
            {
                return new float[0];
            }
            return vArray;
        }

        //ToDouble ==========================================
        public static double[] ToDoubleArray(string str, char sp)
        {
            string[] strArray = str.Split(sp);
            int l = strArray.Length;
            double[] vArray = new double[l];
            int lp;
            try
            {
                for (lp = 0; lp < l; lp++)
                {
                    vArray[lp] = System.Convert.ToDouble(strArray[lp]);
                }
            }
            catch
            {
                return new double[0];
            }
            return vArray;
        }

        //ToDecimal ==========================================
        public static decimal[] ToDecimalArray(string str, char sp)
        {
            string[] strArray = str.Split(sp);
            int l = strArray.Length;
            decimal[] vArray = new decimal[l];
            int lp;
            try
            {
                for (lp = 0; lp < l; lp++)
                {
                    vArray[lp] = System.Convert.ToDecimal(strArray[lp]);
                }
            }
            catch
            {
                return new decimal[0];
            }
            return vArray;
        }

        //ToBool ==========================================
        public static bool[] ToBoolArray(string str, char sp)
        {
            string[] strArray = str.Split(sp);
            int l = strArray.Length;
            bool[] vArray = new bool[l];
            int lp;
            try
            {
                for (lp = 0; lp < l; lp++)
                {
                    if (System.Convert.ToInt32(strArray[lp]) == 0)
                    {
                        vArray[lp] = false;
                    }
                    else
                    {
                        vArray[lp] = true;
                    }
                }
            }
            catch
            {
                return new bool[0];
            }
            return vArray;
        }

    }

}
