﻿using System.Net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.IO;
using System.Timers;
using NsisoLauncherCore.Modules;
using System.Collections.Concurrent;

namespace NsisoLauncherCore.Net
{
    public class DownloadProgressChangedArg : EventArgs
    {
        public int TaskCount { get; set; }
        public int LastTaskCount { get; set; }
        public DownloadTask DoneTask { get; set; }
    }

    public class DownloadSpeedChangedArg : EventArgs
    {
        public decimal SpeedValue { get; set; }
        public string SpeedUnit { get; set; }
    }

    public class DownloadCompletedArg : EventArgs
    {
        public Dictionary<DownloadTask, Exception> ErrorList { get; set; }
    }

    public class MultiThreadDownloader
    {
        private readonly object removeLock = new object();

        /// <summary>
        /// 初始化一个多线程下载器
        /// </summary>
        public MultiThreadDownloader()
        {
            _timer.Elapsed += _timer_Elapsed;
        }

        /// <summary>
        /// 每秒触发事件（下载速度计算）
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void _timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            DownloadSpeedChangedArg arg = new DownloadSpeedChangedArg();
            if (_downloadSizePerSec > 1048576)
            {
                arg.SpeedUnit = "MB/s";
                arg.SpeedValue = Math.Round((decimal)_downloadSizePerSec / (decimal)1048576, 2);
                DownloadSpeedChanged?.Invoke(this, arg);

            }
            else if (_downloadSizePerSec > 1024)
            {
                arg.SpeedUnit = "KB/s";
                arg.SpeedValue = Math.Round((decimal)_downloadSizePerSec / (decimal)1024, 2);
                DownloadSpeedChanged?.Invoke(this, arg);
            }
            else
            {
                arg.SpeedUnit = "B/s";
                arg.SpeedValue = _downloadSizePerSec;
                DownloadSpeedChanged?.Invoke(this, arg);
            }
            _downloadSizePerSec = 0;
        }

        public int ProcessorSize { get; set; } = 5;
        public bool IsBusy { get; private set; } = false;
        public WebProxy Proxy { get; set; }

        public event EventHandler<DownloadProgressChangedArg> DownloadProgressChanged;
        public event EventHandler<DownloadSpeedChangedArg> DownloadSpeedChanged;
        public event EventHandler<DownloadCompletedArg> DownloadCompleted;
        public event EventHandler<Log> DownloadLog;

        public IEnumerable<DownloadTask> DownloadTasks { get => _viewDownloadTasks.AsEnumerable(); }

        private System.Timers.Timer _timer = new System.Timers.Timer(1000);
        private ConcurrentBag<DownloadTask> _downloadTasks;
        private List<DownloadTask> _viewDownloadTasks;
        private readonly object _viewDownloadLocker = new object();
        private int _taskCount;
        private volatile bool _shouldStop = false;
        private Thread[] _threads;
        private int _downloadSizePerSec;
        private Dictionary<DownloadTask, Exception> _errorList = new Dictionary<DownloadTask, Exception>();

        /// <summary>
        /// 设置一群下载内容
        /// </summary>
        /// <param name="tasks"></param>
        public void SetDownloadTasks(List<DownloadTask> tasks)
        {
            if (!IsBusy)
            {
                _downloadTasks = new ConcurrentBag<DownloadTask>(tasks);
                _viewDownloadTasks = new List<DownloadTask>(tasks);
                _taskCount = tasks.Count;
            }
        }

        /// <summary>
        /// 设置一个下载内容
        /// </summary>
        /// <param name="task"></param>
        public void SetDownloadTasks(DownloadTask task)
        {
            if (!IsBusy)
            {
                _downloadTasks = new ConcurrentBag<DownloadTask>();
                _downloadTasks.Add(task);
                _viewDownloadTasks = new List<DownloadTask>();
                _viewDownloadTasks.Add(task);
                _taskCount = 1;
            }

        }

        /// <summary>
        /// 从预览列表中移除一项下载
        /// </summary>
        /// <param name="task"></param>
        private void RemoveItemFromViewTask(DownloadTask task)
        {
            lock (_viewDownloadLocker)
            {
                _viewDownloadTasks.Remove(task);
            }
        }

        /// <summary>
        /// 申请停止
        /// </summary>
        public void RequestStop()
        {
            _shouldStop = true;
            CompleteDownload();
            ApendDebugLog("已申请取消下载");
        }

        /// <summary>
        /// 开始下载
        /// </summary>
        public void StartDownload()
        {
            try
            {
                if (!IsBusy)
                {
                    _shouldStop = false;
                    IsBusy = true;
                    if (ProcessorSize == 0)
                    {
                        IsBusy = false;
                        throw new ArgumentException("下载器的线程数不能为0");
                    }
                    if (_downloadTasks == null || _downloadTasks.Count == 0)
                    {
                        IsBusy = false;
                        return;
                    }
                    //foreach (var item in _downloadTasks.Reverse())
                    //{
                    //    TasksObservableCollection.Add(item);
                    //}
                    _threads = new Thread[ProcessorSize];
                    _timer.Start();
                    for (int i = 0; i < ProcessorSize; i++)
                    {
                        _threads[i] = new Thread(() =>
                        {
                            ThreadDownloadWork();
                        });
                        _threads[i].Name = string.Format("下载线程{0}号", i);
                        _threads[i].Start();
                    }
                    var checkThread = new Thread(() =>
                    {
                        try
                        {
                            while (true)
                            {
                                if (GetAvailableThreadsCount() == 0)
                                {
                                    CompleteDownload();
                                    DownloadCompleted?.Invoke(this, new DownloadCompletedArg() { ErrorList = _errorList });
                                    break;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            SendFatalLog(ex, "下载监视线程发生异常");
                        }
                    });
                    checkThread.Name = "下载监视线程";
                    checkThread.Start();
                }
            }
            catch (Exception ex)
            {
                SendFatalLog(ex, "下载线程分配时发生异常");
            }
        }

        private void ThreadDownloadWork()
        {
            try
            {
                DownloadTask item = null;
                while (!_downloadTasks.IsEmpty)
                {
                    if (_downloadTasks.TryTake(out item))
                    {
                        if (_shouldStop)
                        {
                            CompleteDownload();
                            return;
                        }
                        ApendDebugLog("开始下载:" + item.From);
                        HTTPDownload(item);
                        ApendDebugLog("下载完成:" + item.From);
                        item.SetDone();
                        RemoveItemFromViewTask(item);
                        DownloadProgressChanged?.Invoke(this, new DownloadProgressChangedArg() { TaskCount = _taskCount, LastTaskCount = _viewDownloadTasks.Count, DoneTask = item });
                        //TasksObservableCollection.Remove(item);
                    }
                }
            }
            catch (Exception ex)
            {
                SendFatalLog(ex, "下载线程工作时发生异常");
            }

        }

        private void HTTPDownload(DownloadTask task)
        {
            string realFilename = task.To;
            string buffFilename = realFilename + ".downloadtask";
            try
            {
                if (Path.IsPathRooted(realFilename))
                {
                    string dirName = Path.GetDirectoryName(realFilename);
                    if (!Directory.Exists(dirName))
                    {
                        Directory.CreateDirectory(dirName);
                    }
                }
                if (_shouldStop)
                {
                    CompleteDownload();
                    return;
                }
                HttpWebRequest request = WebRequest.Create(task.From) as HttpWebRequest;
                if (Proxy != null)
                {
                    request.Proxy = Proxy;
                }
                HttpWebResponse response = request.GetResponse() as HttpWebResponse;
                task.SetTotalSize(response.ContentLength);
                Stream responseStream = response.GetResponseStream();
                FileStream fs = new FileStream(buffFilename, FileMode.Create);
                byte[] bArr = new byte[1024];
                int size = responseStream.Read(bArr, 0, (int)bArr.Length);

                while (size > 0)
                {
                    if (_shouldStop)
                    {
                        fs.Close();
                        responseStream.Close();
                        CompleteDownload();
                        return;
                    }
                    fs.Write(bArr, 0, size);
                    size = responseStream.Read(bArr, 0, (int)bArr.Length);
                    _downloadSizePerSec += size;
                    task.IncreaseDownloadSize(size);
                }
                fs.Close();
                responseStream.Close();
                File.Move(buffFilename, realFilename);
            }
            catch (Exception e)
            {
                ApendErrorLog(e);
                if (!_errorList.ContainsKey(task))
                {
                    _errorList.Add(task, e);
                }
            }
        }

        private void CompleteDownload()
        {
            //TasksObservableCollection.Clear();
            _timer.Stop();
            _taskCount = 0;
            _downloadSizePerSec = 0;
            IsBusy = false;
            ApendDebugLog("全部下载任务已完成");
        }

        private void ApendDebugLog(string msg)
        {
            this.DownloadLog?.Invoke(this, new Log() { LogLevel = LogLevel.DEBUG, Message = msg });
        }

        private void ApendErrorLog(Exception e)
        {
            this.DownloadLog?.Invoke(this, new Log() { LogLevel = LogLevel.ERROR, Message = "下载文件失败:" + e.ToString() });
        }

        private int GetAvailableThreadsCount()
        {
            int num = 0; ;
            foreach (var item in _threads)
            {
                if (item != null && item.IsAlive)
                {
                    num += 1;
                }
            }
            return num;
        }

        private void SendLog(Log e)
        {
            DownloadLog?.Invoke(this, e);
        }

        private void SendFatalLog(Exception ex, string msg)
        {
            SendLog(new Log() { Exception = ex, LogLevel = LogLevel.FATAL, Message = msg });
        }

        private void SendDownloadErrLog(DownloadTask task, Exception ex)
        {
            SendLog(new Log() { Exception = ex, LogLevel = LogLevel.ERROR, Message = string.Format("任务{0}下载失败,源地址:{1}原因:{2}", task.TaskName, task.From, ex.Message) });
        }
    }
}
