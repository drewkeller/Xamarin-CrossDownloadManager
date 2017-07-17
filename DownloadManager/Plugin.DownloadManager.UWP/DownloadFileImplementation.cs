﻿using Plugin.DownloadManager.Abstractions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using Windows.Networking.BackgroundTransfer;
using Windows.Storage;

namespace Plugin.DownloadManager
{
    public class DownloadFileImplementation : IDownloadFile
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public event EventHandler DeleteFileRequested;

        public DownloadOperation DownloadOperation;

        private CancellationTokenSource _cancellationToken;

        public string Url { get; }

        public IDictionary<string, string> Headers { get; }

        DownloadFileStatus _status;

        public DownloadFileStatus Status {
            get {
                return _status;
            }
            set
            {
                if (Equals(_status, value)) return;
                _status = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
            }
        }

        string _statusDetails;

        public string StatusDetails
        {
            get {
                return _statusDetails;
            }
            set
            {
                if (Equals(_statusDetails, value)) return;
                _statusDetails = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusDetails)));
            }
        }

        private float _totalBytesExpected;

        public float TotalBytesExpected
        {
            get {
                return _totalBytesExpected;
            }
            set
            {
                if (Equals(_totalBytesExpected, value)) return;
                _totalBytesExpected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalBytesExpected)));
            }
        }

        private float _totalBytesWritten;

        public float TotalBytesWritten
        {
            get {
                return _totalBytesWritten;
            }
            set
            {
                if (Equals(_totalBytesWritten, value)) return;
                _totalBytesWritten = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalBytesWritten)));
            }
        }

        public DownloadFileImplementation(string url, IDictionary<string, string> headers)
        {
            Url = url;
            Headers = headers;

            Status = DownloadFileStatus.INITIALIZED;
        }

        public DownloadFileImplementation(DownloadOperation downloadOperation)
        {
            DownloadOperation = downloadOperation;

            var progress = new Progress<DownloadOperation>(ProgressChanged);
            _cancellationToken = new CancellationTokenSource();

            DownloadOperation.AttachAsync().AsTask(_cancellationToken.Token, progress);
        }

        internal async void StartDownloadAsync(string destinationPathName, bool mobileNetworkAllowed)
        {
            var downloader = new BackgroundDownloader();

            var downloadUrl = new Uri(Url);

            if (Headers != null)
            {
                foreach (var header in Headers)
                {
                    downloader.SetRequestHeader(header.Key, header.Value);
                }
            }

            if (!mobileNetworkAllowed)
            {
                downloader.CostPolicy = BackgroundTransferCostPolicy.UnrestrictedOnly;
            }

            StorageFile file;
            if (destinationPathName != null)
            {
                var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(destinationPathName));
                file = await folder.CreateFileAsync(Path.GetFileName(destinationPathName), CreationCollisionOption.ReplaceExisting);
            }
            else
            {
                var folder = Windows.ApplicationModel.Package.Current.InstalledLocation;
                file = await folder.CreateFileAsync(downloadUrl.Segments.Last(), CreationCollisionOption.GenerateUniqueName);
            }

            DownloadOperation = downloader.CreateDownload(downloadUrl, file);

            var progress = new Progress<DownloadOperation>(ProgressChanged);
            _cancellationToken = new CancellationTokenSource();

            var completed = await DownloadOperation.StartAsync().AsTask(_cancellationToken.Token, progress);
            Status = completed.Progress.Status.ToDownloadFileStatus();
            RaiseDownloadCompletionEvent(Status);
        }

        private void ProgressChanged(DownloadOperation downloadOperation)
        {
            TotalBytesExpected = downloadOperation.Progress.TotalBytesToReceive;
            TotalBytesWritten = downloadOperation.Progress.BytesReceived;

            Status = downloadOperation.Progress.Status.ToDownloadFileStatus();
            RaiseDownloadCompletionEvent(Status);
        }

        private void RaiseDownloadCompletionEvent(DownloadFileStatus status)
        {
            if (status == DownloadFileStatus.COMPLETED || 
                status == DownloadFileStatus.FAILED || 
                status == DownloadFileStatus.CANCELED)
            {
                DeleteFileRequested?.Invoke(this, null);
            }
        }
        
        internal void Cancel()
        {
            Status = DownloadFileStatus.CANCELED;
            if (_cancellationToken != null)
            {
                _cancellationToken.Cancel();
                _cancellationToken = null;
            }
        }
    }
}
