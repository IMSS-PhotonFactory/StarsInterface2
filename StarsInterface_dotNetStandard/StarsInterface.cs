using System;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace STARS
{
    public class StarsInterface : IDisposable
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
        public bool IsConnected { private set; get; } = false;

        //fields
        private int defaultTimeout = 30000;
        private Socket sock;

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
                IPHostEntry ipHostInfo = Dns.GetHostEntry(ServerHostname);
                IPAddress ipAddress = ipHostInfo.AddressList[0];
                for (int i = 0; i < ipHostInfo.AddressList.Length; i++)
                {
                    if(ipHostInfo.AddressList[i].AddressFamily == AddressFamily.InterNetwork)
                    {
                        ipAddress = ipHostInfo.AddressList[i];
                        break;
                    }
                }
                IPEndPoint remoteEP = new IPEndPoint(ipAddress, ServerPort);
                sock = new Socket(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                sock.Connect(remoteEP);
            }
            catch (Exception e)
            {
                throw new StarsException("Could not establish TCP/IP connection.: " + e.Message);
            }

            //Get random number.
            var mes = Receive().from;
            int rNum;
            if(!int.TryParse(mes, out rNum))
            {
                throw new StarsException("Could not establish TCP/IP connection.: " + mes);
            }

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
                StateObject state = new StateObject();
                state.workSocket = sock;

                sock.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0, new AsyncCallback(ReceiveCallback), state);
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
            if(sock != null)
            {
                sock.Disconnect(true);
            }
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
        public StarsMessage Receive()
        {
            return Receive(defaultTimeout);
        }

        public StarsMessage Receive(int timeout)
        {
            bool isFinished = false;

            StateObject state = new StateObject();
            sock.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout, timeout);

            while (!isFinished)
            {
                try
                {
                    int bytesRead = sock.Receive(state.buffer, SocketFlags.None);
                    if (bytesRead > 0)
                    {
                        state.sb.Append(Encoding.Default.GetString(state.buffer, 0, bytesRead));

                        if (state.sb.ToString().Contains("\n"))
                        {
                            isFinished = true;
                        }
                    }
                    else
                    {
                        throw new StarsException($"Could not read.: {bytesRead.ToString()}");
                    }
                }
                catch (Exception e)
                {
                    throw new StarsException($"Could not read.: {e.ToString()}");
                }
            }

            var mes = state.sb.ToString().Substring(0, state.sb.ToString().IndexOf("\n") + 1).Replace("\r", string.Empty).Replace("\n", string.Empty);
            return MessageConverter(mes);
        }

        //Callback
        private  void ReceiveCallback(IAsyncResult ar)
        {
            try
            {
                StateObject state = (StateObject)ar.AsyncState;
                Socket client = state.workSocket;

                int bytesRead = client.EndReceive(ar);

                if (bytesRead > 0)
                {
                    state.sb.Append(Encoding.Default.GetString(state.buffer, 0, bytesRead));

                    while (state.sb.ToString().Contains("\n"))
                    {
                        var mes = state.sb.ToString().Substring(0, state.sb.ToString().IndexOf("\n") + 1).Replace("\r", string.Empty).Replace("\n", string.Empty);
                        OnDataReceived(new StarsCbArgs(MessageConverter(mes)));

                        state.sb.Remove(0, state.sb.ToString().IndexOf("\n") + 1);
                    }
                }
                else
                {
                    return;
                }

                client.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0, new AsyncCallback(ReceiveCallback), state);
            }
            catch (Exception e)
            {
                throw new StarsException($"Error at ReceiveCallback: {e.ToString()}"); ;
            }
        }

        private StarsMessage MessageConverter(string str)
        {
            if(str.IndexOf(">") < 0)
            {
                return new StarsMessage(str, "", "", "");
            }
            
            var from = str.Substring(0, str.IndexOf(">")).TrimEnd();
            str = str.Substring(str.IndexOf(">") + 1).TrimStart();

            if (str.IndexOf(" ") < 0)
            {
                return new StarsMessage(from, str, "", "");
            }

            var to = str.Substring(0, str.IndexOf(" "));
            str = str.Substring(str.IndexOf(" ") + 1).TrimStart();

            if (str.IndexOf(" ") < 0)
            {
                return new StarsMessage(from, to, str, "");
            }

            var command = str.Substring(0, str.IndexOf(" "));
            var parameters = str.Substring(str.IndexOf(" ") + 1).TrimStart();

            return new StarsMessage(from, to, command, parameters);
        }

        public event EventHandler<StarsCbArgs> DataReceived;
        protected virtual void OnDataReceived(StarsCbArgs e)
        {
            DataReceived?.Invoke(this, e);
        }

        public void Dispose()
        {
            ((IDisposable)sock).Dispose();
        }

        ~StarsInterface()
        {
            Dispose();
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

    internal class StateObject
    {
        public Socket workSocket = null;
        public const int BufferSize = 8192;
        public byte[] buffer = new byte[BufferSize];
        public StringBuilder sb = new StringBuilder();
    }

    public class StarsCbArgs : EventArgs
    {
        public StarsMessage STARS;

        public string from {
            get {
                return STARS.from;
            }
        }
            
        public string to {
            get {
                return STARS.to;
            }
        }

        public string command {
            get {
                return STARS.command;
            }
        }

        public string parameters {
            get {
                return STARS.parameters;
            }
        }

        public StarsCbArgs(string from, string to, string command, string parameters)
        {
            STARS = new StarsMessage(from, to, command, parameters);
        }

        public StarsCbArgs(StarsMessage starsmessage)
        {
            STARS = starsmessage;
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
