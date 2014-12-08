namespace SamNoble.Wpf.BackgroundProcessing
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.ComponentModel;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Windows;

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        #region [ Fields ]

        private BackgroundWorker backgroundWorker;
        private CancellationTokenSource cancellationTokenSource;

        private ObservableCollection<string> data;
        private ObservableCollection<Tuple<DateTime, string>> log;
        private string processingTime;

        #endregion

        #region [ Constructors ]

        public MainWindow()
        {
            InitializeComponent();

            // Allow binding to the Log and Data properties in lieu of a view model.
            this.DataContext = this;
        }

        #endregion

        #region [ Events ]

        public event PropertyChangedEventHandler PropertyChanged;

        #endregion

        #region [ Properties ]

        public ObservableCollection<string> Data
        {
            get { return this.data ?? (this.data = new ObservableCollection<string>()); }
        }

        public ObservableCollection<Tuple<DateTime, string>> Log
        {
            get {  return this.log ?? (this.log = new ObservableCollection<Tuple<DateTime,string>>()); }
        }

        public string ProcessingTime
        {
            get
            {
                return this.processingTime;
            }
            private set
            {
                if (this.processingTime != value)
                {
                    this.processingTime = value;
                    this.OnPropertyChanged("ProcessingTime");
                }
            }
        }

        #endregion

        #region [ UI Event Handlers ]

        private void StartProcess_Click(object sender, RoutedEventArgs e)
        {
            this.ProcessingTime = "Calculating...";

            this.RunBackgroundWorkerStyle();
        }

        private async void StartProcess2_Click(object sender, RoutedEventArgs e)
        {
            this.ProcessingTime = "Calculating...";

            var time = await this.RunAsyncAwaitStyle();

            this.ProcessingTime = string.Format("{0} seconds", time.TotalSeconds);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.WriteLog("Cancellation requested...");

            if (this.cancellationTokenSource != null)
            {
                this.cancellationTokenSource.Cancel();
            }
            else
            {
                this.backgroundWorker.CancelAsync();
            }
        }

        #endregion

        #region [ BackgroundWorker Approach ]

        private void RunBackgroundWorkerStyle()
        {
            this.WriteLog("Starting process with BackgroundWorker...");

            this.ProgressLayer.Visibility = Visibility.Visible;
            this.ProgressBar.Value = 0;

            // Make sure we specify that we support progress reporting and cancellation.
            this.backgroundWorker = new BackgroundWorker()
            {
                WorkerReportsProgress = true,
                WorkerSupportsCancellation = true
            };

            // Handler for progress changed events. Update the ProgressBar,
            // process the data and log an event.
            this.backgroundWorker.ProgressChanged += (s, pe) =>
            {
                var batch = pe.UserState as IEnumerable<string>;

                this.WriteLog("Progress updated to {0}%. Received {1} items.", pe.ProgressPercentage, batch.Count());

                foreach (var item in batch)
                {
                    this.data.Add(item);
                }

                this.ProgressBar.Value = pe.ProgressPercentage;
            };

            this.backgroundWorker.DoWork += (s, pe) =>
            {
                var startTime = DateTime.UtcNow;
                var worker = s as BackgroundWorker;
                var parameters = pe.Argument as Tuple<int, int, int>;
                var pageProgress = 100.0 / ((double)parameters.Item2 / (double)parameters.Item3);
                var progress = 0;

                for (var i = parameters.Item1; i < parameters.Item1 + parameters.Item2; i += parameters.Item3)
                {
                    if (worker.CancellationPending)
                    {
                        pe.Cancel = true;
                        return;
                    }

                    var batch = SuperSlowWebService.FetchResults(i, parameters.Item3);

                    progress += (int)Math.Round(pageProgress);
                    worker.ReportProgress(progress, batch);
                }

                pe.Result = DateTime.UtcNow - startTime;
            };

            // When the worker is finished, tidy up.
            this.backgroundWorker.RunWorkerCompleted += (s, pe) =>
            {
                if (!pe.Cancelled)
                {
                    var time = (TimeSpan)pe.Result;
                    this.ProcessingTime = string.Format("{0} seconds", time.TotalSeconds);
                }
                else
                {
                    this.ProcessingTime = "Unknown";
                }

                this.ProgressLayer.Visibility = Visibility.Collapsed;

                this.WriteLog("Background worker process complete. Operation was {0}.", pe.Cancelled ? "cancelled" : "successful");

                this.backgroundWorker.Dispose();
                this.backgroundWorker = null;
            };

            this.backgroundWorker.RunWorkerAsync(new Tuple<int, int, int>(100, 500, 100));
        }

        #endregion

        #region [ Async / Await Approach ]

        private async Task<TimeSpan> RunAsyncAwaitStyle()
        {
            this.WriteLog("Starting process with async / await.");

            this.ProgressLayer.Visibility = Visibility.Visible;
            this.ProgressBar.Value = 0;

            var cancelled = false;
            var processingTime = TimeSpan.FromSeconds(0);

            try
            {
                this.cancellationTokenSource = new CancellationTokenSource();

                processingTime = await this.FetchItems(100, 500, 100, this.cancellationTokenSource.Token, new Progress<ProgressReport<IEnumerable<string>>>(this.ReportProgress));
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }

            this.cancellationTokenSource = null;

            this.WriteLog("Background worker process complete. Operation was {0}.", cancelled ? "cancelled" : "successful");

            this.ProgressLayer.Visibility = Visibility.Collapsed;

            return processingTime;
        }

        private async Task<TimeSpan> FetchItems(int offset, int count, int pageSize, CancellationToken cancellationToken, IProgress<ProgressReport<IEnumerable<string>>> progressReporter)
        {
            return await Task.Run(() => 
            {
                var startTime = DateTime.UtcNow;
                var pageProgress = 100.0 / ((double)count / (double)pageSize);
                var progress = 0;

                for (var i = offset; i < offset + count; i += pageSize)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var batch = SuperSlowWebService.FetchResults(i, pageSize);

                    progress += (int)Math.Round(pageProgress);
                    progressReporter.Report(new ProgressReport<IEnumerable<string>>() { ProgressPercentage = progress, UserState = batch });
                }

                return DateTime.UtcNow - startTime;
            },
            cancellationToken);
        }

        private void ReportProgress(ProgressReport<IEnumerable<string>> progress)
        {
            this.WriteLog("Progress updated to {0}%. Received {1} items.", progress.ProgressPercentage, progress.UserState.Count());

            foreach (var item in progress.UserState)
            {
                this.data.Add(item);
            }

            this.ProgressBar.Value = progress.ProgressPercentage;
        }

        internal class ProgressReport<T>
        {
            public int ProgressPercentage { get; set; }
            public T UserState { get; set; }
        }

        #endregion

        #region [ Utility Methods ]

        private void WriteLog(string message, params object[] args)
        {
            this.Log.Insert(0, new Tuple<DateTime, string>(DateTime.Now, string.Format(message, args)));
        }

        private void OnPropertyChanged(string propertyName)
        {
            var handler = this.PropertyChanged;

            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        #endregion
    }

    public static class SuperSlowWebService
    {
        public static IEnumerable<string> FetchResults(int offset, int count)
        {
            var items = new List<string>();

            for (var i = offset; i < offset + count; i++)
            {
                items.Add(string.Format("Item {0}", i));
            }

            Thread.Sleep(TimeSpan.FromSeconds(5));

            return items;
        }
    }
}
