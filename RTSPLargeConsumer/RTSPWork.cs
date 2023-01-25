using RtspClientExample;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RTSPLargeConsumer
{
    internal class RTSPWork
    {
        // We hold the class where we can access the fps values
        internal RTSPworker workerClass;
        // And the thread where we can deduce if its a live
        internal Thread workerThread;
        public RTSPWork(string url, CancellationToken cancellationToken)
        {
            workerClass = new RTSPworker(url, cancellationToken);
            workerThread = new Thread(new ThreadStart(workerClass.Execute));
            workerThread.Start();
        }
    }
    internal class RTSPworker
    {
        public string url;
        private int _threadId;
        internal int cntH264, cntH265, cntNal, cntG711, cntAmr, cntAcc;

        CancellationToken _cancellationToken;
        internal RTSPClient client = new RTSPClient();

        public RTSPworker(string url, CancellationToken cancellationToken)
        {
            this.url = url;
            _cancellationToken = cancellationToken;
        }
        internal void Execute()
        {
            _threadId = System.Threading.Thread.CurrentThread.ManagedThreadId;

            client.Received_SPS_PPS += (byte[] sps, byte[] pps) =>
            {
                cntH264++;
            };
            client.Received_VPS_SPS_PPS += (byte[] vps, byte[] sps, byte[] pps) =>
            {
                cntH265++;
            };
            client.Received_NALs += (List<byte[]> nal_units) =>
            {
                cntNal++;
            };
            client.Received_G711 += (string format, List<byte[]> g711) =>
            {
                cntG711++;
            };
            client.Received_AMR += (string format, List<byte[]> amr) =>
            {
                cntAmr++;
            };
            client.Received_AAC += (string format, List<byte[]> aac, uint ObjectType, uint FrequencyIndex, uint ChannelConfiguration) =>
            {
                cntAcc++;
            };

            try
            {
                do
                {
                    client.Connect(url, RTSPClient.RTP_TRANSPORT.TCP, RTSPClient.MEDIA_REQUEST.VIDEO_AND_AUDIO);
                    //client.Play();

                    while (!client.StreamingFinished() && !_cancellationToken.IsCancellationRequested)
                        Thread.Sleep(500);

                    if (_cancellationToken.IsCancellationRequested)
                        client.Stop();
                    //else
                    //    Console.WriteLine($"{_threadId}: Reconnecting");
                }
                while (!_cancellationToken.IsCancellationRequested);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{_threadId}: Thread died with error: {ex.Message}");
            }
        }
    }
}
