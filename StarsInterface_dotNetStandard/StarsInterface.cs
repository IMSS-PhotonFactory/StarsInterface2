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
        private bool disposedValue;

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
                IPAddress[] ipAddress = Dns.GetHostAddresses(ServerHostname);
                var remoteEP = new IPEndPoint(ipAddress[0], ServerPort);
                sock = new Socket(remoteEP.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
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
            tcpSendString($"{sndFrom}>{sndTo} {sndCommand}");
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

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    sock.Dispose();
                }

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }

    public class StarsMessage
    {
        public string from { private set; get; } = "";
        public string to { private set; get; } = "";
        public string command { private set; get; } = "";
        public string parameters { private set; get; } = "";

        public StarsMessage(string from, string to, string command, string parameters)
        {
            this.from = from;
            this.to = to;
            this.command = command;
            this.parameters = parameters;
        }

        public StarsMessage() : this("", "", "", "") { }

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

        public string parameters
        {
            get
            {
                return STARS.parameters;
            }
        }

        public string allMessage
        {
            get
            {
                return STARS.allMessage;
            }
        }

        public string Message
        {
            get
            {
                return STARS.Message;
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
}
