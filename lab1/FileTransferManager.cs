using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using System.IO;

namespace lab1
{
    public class FileTransferManager
    {
        private static readonly byte MAGIC_1 = 0x46;
        private static readonly byte MAGIC_2 = 0x53;

        private static readonly byte PACKET_ACK = 0x30;
        private static readonly byte PACKET_REQ = 0x31;
        private static readonly byte PACKET_DAT = 0x32;
        //private static readonly byte REQUEST_ACK = 0x46;

        private static readonly int TRIES = 3;
        // 10s - debug
        private static readonly int TRY_DELAY = 10000; // ms

        readonly Func<string, bool, int> onAdd;
        readonly Action<int, int> onUpdate;
        readonly Action<int> onComplete;
        readonly Action<int> onFail;

        public FileTransferManager(Func<string, bool, int> onAdd, // filename, (true = send, false = receive) -> id
            Action<int, int> onUpdate, // id, percentage -> void
            Action<int> onComplete,
            Action<int> onFail)
        {
            this.onAdd = onAdd;
            this.onUpdate = onUpdate;
            this.onComplete = onComplete;
            this.onFail = onFail;
        }

        private static Random rng = new Random();

        private UdpClient requestReciever;
        private Thread recieverThread;
        //private List<Thread> workThreads = new List<Thread>();

        // called by GUI - transfer file to...
        public void transferFile(string filename, string address, int port)
        {
            var id = onAdd(filename, true);
            FileTransferSender sender = new FileTransferSender(this, filename, address, port, id);
            Thread t = new Thread(new ThreadStart(sender.sender));
            t.IsBackground = true;
            //workThreads.Add(t);
            t.Start();
        }

        // called by GUI - checkbox set
        public void startRecieving(int port)
        {
            requestReciever = new UdpClient(port);
            recieverThread = new Thread(new ThreadStart(reciever));
            recieverThread.IsBackground = true;
            recieverThread.Start();
        }

        // called by GUI - checkbox unset
        public void stopRecieving()
        {
            if (requestReciever != null)
            {
                requestReciever.Close();
                recieverThread.Abort();
            }
        }

        /* a thread method
         * it recieves requests, acknowledges them
         * and launches the FileTransferReciever thread
         */
        private void reciever()
        {
            while (true)
            {
                IPEndPoint host = null;
                var request = requestReciever.Receive(ref host);

                if (!checkPacket(request, PACKET_REQ))
                    return;

                // bytes [3..6] - sequence number

                int chunks = BitConverter.ToInt32(request, 7);

                int filenameLength = request[11];
                string filename = System.Text.Encoding.UTF8.GetString(request, 12, filenameLength);


                // select a "random" port
                Int32 port = rng.Next(5060, 6060);
                UdpClient recieverSocket = new UdpClient(port);
                recieverSocket.Client.ReceiveTimeout = TRY_DELAY;

                var response = new byte[11];
                // 0, 1: magic
                response[0] = MAGIC_1;
                response[1] = MAGIC_2;
                // 2: "Acknowledge"
                response[2] = PACKET_ACK;
                // 3 - 6: sequence number
                for (int i = 0; i != 4; ++i)
                {
                    response[3 + i] = request[3 + i];
                }
                // 7 - 10: port number
                var port_ser = BitConverter.GetBytes(port);
                for (int i = 0; i != 4; ++i)
                {
                    response[7 + i] = port_ser[i];
                }

                // slight problem: what if the acknowledgement doesn't arrive?
                // then there should be a check "don't alarm the GUI again"
                requestReciever.Send(response, 11, host);
                var transferId = onAdd(filename, false);
                var ftr = new FileTransferReciever(this, filename, recieverSocket, chunks, transferId);
                Thread t = new Thread(new ThreadStart(ftr.reciever));
                t.IsBackground = true;
                t.Start();

                // this metthod
                /*
                 * 1. recieves the transfer request
                 * 2. negotiates the port
                 * 3. creates a new udpclient
                 * 4. creates a filetranferreciever, which will actually transfer
                 */
            }
        }

        // check the recieved packet
        // conditions:
        // 1. magic numbers (first two bytes)
        // 2. type == expectedType
        private static bool checkPacket(byte[] data, int expectedType)
        {
            // magic
            if (!((data[0] == MAGIC_1) && (data[1] == MAGIC_2)))
                return false;

            // packet type
            if (data[2] != expectedType)
                return false;
            return true;
        }

        // sends a packet over to a specified host
        // if host is null, expects socket to be "connected"
        private static KeyValuePair<bool, byte[]> sendPacket(byte[] data, UdpClient socket, IPEndPoint host = null)
        {
            try
            {
                var seqId = BitConverter.GetBytes(rng.Next());

                // really dumb
                for (int i = 3; i != 7; ++i)
                {
                    data[i] = seqId[i - 3];
                }

                IPEndPoint from = null;
                for (int i = 0; i < TRIES; i++)
                {
                    if (host == null)
                    {
                        socket.Send(data, data.Length);
                    }
                    else
                    {
                        socket.Send(data, data.Length, host);
                    }


                    data = socket.Receive(ref from);
                    if (!checkPacket(data, PACKET_ACK))
                        continue;

                    bool correct_seqid = true;
                    for (int j = 3; j != 7; ++j)
                    {
                        if (data[j] != seqId[j - 3])
                        {
                            correct_seqid = false;
                        }
                    }

                    if (correct_seqid)
                    {
                        Thread.Sleep(500);
                        return new KeyValuePair<bool, byte[]>(true, data);
                    }
                    else
                    {
                        // doesn't count
                        i -= 1;
                    }
                }
            }
            catch (Exception) { } // ignore - network failure
            return new KeyValuePair<bool, byte[]>(false, null);
        }

        // thread class
        private class FileTransferReciever
        {
            private readonly FileTransferManager parent;
            private readonly string filename;
            private readonly UdpClient socket;
            private readonly int chunks;
            private readonly int transferId;
            public FileTransferReciever(FileTransferManager parent, string filename, UdpClient socket, int chunks, int transferId)
            {
                this.filename = filename;
                this.socket = socket;
                this.chunks = chunks;
                this.parent = parent;
                this.transferId = transferId;
            }

            public void reciever()
            {
                try
                {
                    var f = File.OpenWrite(filename);

                    int current_chunk = 0;
                    int previous_seq_number = -1;
                    while (current_chunk != chunks)
                    {
                        IPEndPoint other = null;
                        var packet = socket.Receive(ref other);

                        if (!checkPacket(packet, PACKET_DAT))
                        {
                            parent.onFail(transferId);
                            return;
                        }

                        // respond anyway
                        var response = new byte[7];
                        response[0] = MAGIC_1;
                        response[1] = MAGIC_2;
                        response[2] = PACKET_ACK;
                        for (int i = 0; i != 4; i++)
                        {
                            response[3 + i] = packet[3 + i];
                        }
                        socket.Send(response, 7, other);

                        // but write only if this is actually expected
                        var this_seq_number = BitConverter.ToInt32(packet, 3);
                        if (this_seq_number != previous_seq_number)
                        {
                            var data_size = BitConverter.ToInt32(packet, 7);
                            f.Write(packet, 11, data_size);
                            current_chunk += 1;
                            previous_seq_number = this_seq_number;
                            parent.onUpdate(transferId, 100 * (current_chunk + 1) / chunks);
                        }
                    }
                    f.Close();
                    parent.onComplete(transferId);
                }
                catch (Exception)
                {
                    parent.onFail(transferId);
                    return;
                }
            }
        }

        // thread class
        private class FileTransferSender
        {
            private static readonly int MAX_READ_BLOCK = 256; // bytes

            private readonly string filename, address;
            private readonly int port;
            private readonly FileTransferManager parent;
            private readonly int transferId;

            public FileTransferSender(FileTransferManager parent, string filename, string address, int port, int transferId)
            {
                this.filename = filename;
                this.address = address;
                this.port = port;
                this.parent = parent;
                this.transferId = transferId;
            }

            public void sender()
            {
                try
                {
                    // part 0: get info about the file
                    int chunks = (int)Math.Ceiling(1.0 * new FileInfo(filename).Length / MAX_READ_BLOCK);
                    // part I: send a request
                    UdpClient request_client = new UdpClient();
                    request_client.Connect(address, port);
                    request_client.Client.ReceiveTimeout = TRY_DELAY;
                    byte[] request = new byte[filename.Length * 2 + 3 + 4 + 4 + 1];
                    request[0] = MAGIC_1;
                    request[1] = MAGIC_2;
                    request[2] = PACKET_REQ;
                    // start from 7: seqId will be filled in by sendPacket()
                    var chunks_ser = BitConverter.GetBytes(chunks);
                    for (int i = 0; i != 4; ++i)
                    {
                        request[7 + i] = chunks_ser[i];
                    }
                    string filename_actual = filename.Split('\\').Last();
                    byte[] t = new UTF8Encoding(false, false).GetBytes(filename_actual);
                    
                    request[11] = (byte)t.Length;
                    for (int i = 0; i != t.Length; ++i)
                    {
                        request[12 + i] = t[i];
                    }
                    var response = sendPacket(request, request_client);
                    if (!response.Key)
                    {
                        parent.onFail(transferId);
                        return;
                    }
                    // part II: send the actual file
                    int send_port = BitConverter.ToInt32(response.Value, 7);
                    UdpClient dataTransfer_client = new UdpClient(address, send_port);
                    dataTransfer_client.Client.ReceiveTimeout = TRY_DELAY;

                    // prepare the packet structure
                    byte[] data_packet = new byte[MAX_READ_BLOCK + 3 + 4 + 4];
                    data_packet[0] = MAGIC_1;
                    data_packet[1] = MAGIC_2;
                    data_packet[2] = PACKET_DAT;

                    FileStream f = File.OpenRead(filename);
                    for (int i = 0; i != chunks; ++i)
                    {
                        int actually_read = f.Read(data_packet, 11, MAX_READ_BLOCK);
                        var temp = BitConverter.GetBytes(actually_read);
                        for (int j = 0; j != 4; ++j)
                        {
                            data_packet[7 + j] = temp[j];
                        }
                        if (!sendPacket(data_packet, dataTransfer_client).Key)
                        {
                            parent.onFail(transferId);
                            return;
                        }
                        parent.onUpdate(transferId, 100 * (i + 1) / chunks);
                    }
                    f.Close();
                    parent.onComplete(transferId);
                } catch (Exception)
                {
                    parent.onFail(transferId);
                }
            }
        }
    }
}
