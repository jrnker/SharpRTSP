using RtspClientExample;
using System.Timers;

namespace RTSPLargeConsumer
{
    /// <summary>
    /// A console app that reads a local settings file describing what streams to consume.
    /// The settings file is automatically created on first run, and takes either an RTSP url, or 
    /// an RTSP url and a number, separated with a pipe (|) sign. In the latter case then it starts n of
    /// those instances.
    /// Developed for the purpose of loadtesting.
    /// 
    /// Author: Christoffer Järnåker, and is issued with MIT license
    /// Original project: https://github.com/ngraziano/SharpRTSP
    /// </summary>
    internal class Program
    {
        internal static List<RTSPWork> clients = new List<RTSPWork>();
        private static readonly object _lock = new object();
        private static CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private static DateTime _lastMeasurement = DateTime.UtcNow;
        private static int _lastClientCount;
        private static int _maxWaitingClients = 5;

        static void Main(string[] args)
        {
            Console.Clear();

            // Start a timer that executes once a second and displays fps values
            var timer = new System.Timers.Timer(1000);
            timer.Elapsed += DrawToConsole;
            timer.Start();

            // RTSP stream settings file
            var filename = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name + ".txt";

            // Create the template if it doesn't exist
            if (!File.Exists(filename))
            {
                var template = @"// File that descibes what to consume

// Consume one of the following URL
rtsp://wowzaec2demo.streamlock.net/vod/mp4:BigBuckBunny_115k.mp4

// Consume 5 of the following URL, with a | separator at the end
rtsp://wowzaec2demo.streamlock.net/vod/mp4:BigBuckBunny_115k.mp4|5

// This gets a total of 6 streams to consume
";
                File.WriteAllText(filename, template);
            }

            // Get the settings, and clean them up
            var rtspConsume = File.ReadAllText(filename).Replace("\r", "");
            var rtspConsumeList = rtspConsume.Split('\n').ToList();
            rtspConsumeList.RemoveAll(x => x.StartsWith("//"));
            rtspConsumeList.RemoveAll(x => string.IsNullOrEmpty(x));

            // Generate a list to kick off
            var launchList = new List<string>();
            foreach (var line in rtspConsumeList)
            {
                // Is it a single entry or not?
                var split = line.Split('|');
                if (split.Length == 1)
                    // Single entry
                    launchList.Add(split[0]);
                else
                    // Multipe streams from same source
                    for (var n = 0; n < int.Parse(split[1]); n++)
                        launchList.Add(split[0]);
            }

            // Start threads
            foreach (var launchURL in launchList)
            {
                // We lock this as the manipulation of the list of clients can't happen when we're drawing fps valuse
                lock (_lock)
                {
                    // Start a new worker
                    var w = new RTSPWork(launchURL, _cancellationTokenSource.Token);
                    // ..and add it to the list
                    clients.Add(w);
                }

                // We don't want to overload the endpoint, so let's thread carefully
                int notConnected;
                lock (_lock)
                    notConnected = clients.Count(x => x.workerClass.client.GetSocketStatus() != RTSPClient.RTSP_STATUS.Connected);

                while (notConnected > _maxWaitingClients)
                {
                    Thread.Sleep(500);
                    lock (_lock)
                        notConnected = clients.Count(x => x.workerClass.client.GetSocketStatus() != RTSPClient.RTSP_STATUS.Connected);
                }

                // Check if we should cancel
                if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Escape)
                    _cancellationTokenSource.Cancel();

                // If cancellation is requested, exit
                if (_cancellationTokenSource.IsCancellationRequested)
                    break;
            }

            // We just need to wait for the user to press Escape
            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Escape)
                    break;
                Thread.Sleep(500);
            }

            // Send a cancellation
            _cancellationTokenSource.Cancel();

            // And wait for the clients to shut down
            // The cleaning happens in the timer thread
            while (clients.Count > 0)
                Thread.Sleep(100);

            // Stop the timer
            timer.Close();
            Console.WriteLine("Exiting..");
        }
        static void DrawToConsole(Object sender, ElapsedEventArgs eventArgs)
        {
            // If the amount of clients has changed
            if (clients.Count != _lastClientCount)
            {
                _lastClientCount = clients.Count;
                // Clear display if we have less of a count then last time
                if (clients.Count < _lastClientCount)
                    Console.Clear();
            }

            // Start at top of screen
            Console.CursorTop = 0;
            Console.CursorLeft = 0;

            // We lock this as the manipulation of the list of clients can't happen when we're drawing fps valuse
            lock (_lock)
            {
                // Get the gap between now and the last time we displayed the status
                var gap = (DateTime.UtcNow - _lastMeasurement).TotalMilliseconds;
                _lastMeasurement = DateTime.UtcNow;

                // Fix the measurements if we're lagging
                foreach (var c in clients)
                {
                    c.workerClass.cntH264 = (int)(c.workerClass.cntH264 / (gap / 1000));
                    c.workerClass.cntH265 = (int)(c.workerClass.cntH265 / (gap / 1000));
                    c.workerClass.cntNal = (int)(c.workerClass.cntNal / (gap / 1000));
                    c.workerClass.cntG711 = (int)(c.workerClass.cntG711 / (gap / 1000));
                    c.workerClass.cntAmr = (int)(c.workerClass.cntAmr / (gap / 1000));
                    c.workerClass.cntAcc = (int)(c.workerClass.cntAcc / (gap / 1000));
                }

                // Get some values to display at the top
                var zeroFpsClients = clients.Count(x => x.workerClass.cntNal == 0);
                var avgRunning = 0;
                if (clients.Any(x => x.workerClass.cntNal != 0))
                    avgRunning = (int)Math.Ceiling(clients.Where(x => x.workerClass.cntNal != 0).Average(x => x.workerClass.cntNal));


                // Write out the top line
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine($"Clients: {clients.Count}, 0 fps clients {zeroFpsClients}, Avg fps running: {avgRunning.ToString().PadRight(15, ' ')}");

                // Write out the measurements
                foreach (var c in clients)
                {
                    // Change the colour depending on connection status
                    if (c.workerClass.client?.GetSocketConnected() == true && c.workerClass.client?.GetSocketStatus() == RTSPClient.RTSP_STATUS.Connected)
                        Console.ForegroundColor = ConsoleColor.Green;
                    if (c.workerClass.client?.GetSocketStatus() == RTSPClient.RTSP_STATUS.ConnectFailed)
                        Console.ForegroundColor = ConsoleColor.Red;
                    if (c.workerClass.client?.GetSocketStatus() == RTSPClient.RTSP_STATUS.WaitingToConnect)
                        Console.ForegroundColor = ConsoleColor.Yellow;
                    if (c.workerClass.client?.GetSocketStatus() == RTSPClient.RTSP_STATUS.Connecting)
                        Console.ForegroundColor = ConsoleColor.Blue;

                    // Screen output that shows all types, takes lots of space, but is good for troubleshooting
                    //Console.WriteLine($"{c.workerThread.ManagedThreadId.ToString().PadLeft(2, '0')}: H264 {c.workerClass.cntH264} H265 {c.workerClass.cntH265} NAL {c.workerClass.cntNal} G711 {c.workerClass.cntG711} AMR {c.workerClass.cntAmr} AMR {c.workerClass.cntAcc}");

                    // We paint each fps value (based on NAL frames) with 2 chars + space
                    Console.Write($"{c.workerClass.cntNal.ToString().PadLeft(2, '0')} ");

                    // Then reset the counter on this client
                    c.workerClass.cntH264 = 0;
                    c.workerClass.cntH265 = 0;
                    c.workerClass.cntNal = 0;
                    c.workerClass.cntG711 = 0;
                    c.workerClass.cntAmr = 0;
                    c.workerClass.cntAcc = 0;
                }
                Monitor.PulseAll(_lock);

                // Remove clients not alive
                clients.RemoveAll(x => !x.workerThread.IsAlive);
            }
        }
    }
}