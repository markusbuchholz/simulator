/**
 * Copyright (c) 2019 LG Electronics, Inc.
 *
 * This software contains code licensed as described in LICENSE.
 *
 */
using System;
using System.IO;
using System.Net;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using UnityEngine;
using System.Linq;

namespace Simulator.Web
{
    public static class DownloadManager
    {
        class Download
        {
            public Uri uri;
            public string path;
            public Action<int> update;
            public Action<bool> completed;
            public bool valid = true;

            public Download(Uri uri, string path, Action<int> update, Action<bool> completed)
            {
                this.uri = uri;
                this.path = path;
                this.update = update;
                this.completed = completed;
            }

            public void Completed(object sender, AsyncCompletedEventArgs args)
            {
                if (args.Error != null && !cancelled)
                {
                    Debug.LogException(args.Error);
                }

                completed?.Invoke(args.Error == null && !args.Cancelled);

                client.DownloadProgressChanged -= Update;
                client.DownloadFileCompleted -= Completed;
            }

            public void Update(object sender, DownloadProgressChangedEventArgs args)
            {
                if (Time.realtimeSinceStartup < currentTime + 1) return;

                if (currentProgress != args.ProgressPercentage)
                {
                    currentProgress = args.ProgressPercentage;
                    currentTime = Time.time;
                    update?.Invoke(args.ProgressPercentage);
                }
            }
        }

        static ConcurrentQueue<Download> downloads = new ConcurrentQueue<Download>();
        static WebClient client;
        static string currentUrl;
        static int currentProgress;
        static float currentTime;
        static bool cancelled;

        public static void Init()
        {
            client = new WebClient();
            ManageDownloads();
        }

        public static void AddDownloadToQueue(Uri uri, string path, Action<int> update = null, Action<bool> completed = null)
        {
            downloads.Enqueue(new Download(uri, path, update, completed));
        }

        public static void StopDownload(string url)
        {
            if (url == currentUrl)
            {
                cancelled = true;
                client.CancelAsync();
            }
            else
            {
                Download download = downloads.FirstOrDefault(d => d.uri.OriginalString == url);
                if (download == null)
                {
                    throw new Exception($"Cannot remove download from download queue: {url} is not in the download queue.");
                }

                download.valid = false;
            }
        }

        static async void ManageDownloads()
        {
            while (true)
            {
                Download download;
                if (downloads.TryDequeue(out download) && download.valid)
                {
                    currentUrl = download.uri.OriginalString;
                    await DownloadFile(download);
                }

                await Task.Delay(1000);
            }
        }

        static async Task DownloadFile(Download download)
        {
            try
            {
                var fileName = Path.GetFileName(download.uri.AbsolutePath);
                Debug.Log($"Downloading {download.uri.AbsoluteUri}");

                currentProgress = 0;
                currentTime = Time.realtimeSinceStartup;
                client.DownloadProgressChanged += ValidateDownload;
                client.DownloadProgressChanged += download.Update;
                client.DownloadFileCompleted += download.Completed;
                cancelled = false;
                await client.DownloadFileTaskAsync(download.uri, download.path);
            }
            catch
            {
                if (File.Exists(download.path))
                {
                    File.Delete(download.path);
                }
            }
        }

        static void ValidateDownload(object sender, DownloadProgressChangedEventArgs args)
        {
            if (!(client.ResponseHeaders["content-type"].StartsWith("application") || client.ResponseHeaders["content-type"].StartsWith("binary")))
            {
                StopDownload(currentUrl);
                Debug.LogError($"Failed to download: Content-Type {client.ResponseHeaders["content-type"]} not supported.");
            }

            client.DownloadProgressChanged -= ValidateDownload;
        }
    }
}