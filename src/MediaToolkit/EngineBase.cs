using MediaToolkit.Properties;
using MediaToolkit.Util;
using System;
using System.Linq;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading;

namespace MediaToolkit
{
    public class EngineBase : IDisposable
    {
        bool isDisposed;
        const string LockName = "MediaToolkit.Engine.LockName";
        const string DefaultFFmpegFilePath = @"/MediaToolkit/ffmpeg.exe";
        protected readonly string FFmpegFilePath;
        protected readonly Mutex Mutex;
        protected Process FFmpegProcess;


        protected EngineBase()
           : this(ConfigurationManager.AppSettings["mediaToolkit.ffmpeg.path"])
        { }

        /// <summary>
        /// Initializes FFmpeg.exe; Ensuring that there is a copy 
        /// in the clients temp folder &amp; isn't in use by another process.
        /// </summary>
        protected EngineBase(string ffMpegPath)
        {
            Mutex = new Mutex(false, LockName);
            isDisposed = false;

            if (string.IsNullOrWhiteSpace(ffMpegPath))
                ffMpegPath = DefaultFFmpegFilePath;

            FFmpegFilePath = ffMpegPath;

            EnsureDirectoryExists();
            EnsureFFmpegFileExists();
            EnsureFFmpegIsNotUsed();
        }

        void EnsureFFmpegIsNotUsed()
        {
            try
            {
                Mutex.WaitOne();
                var processes = Process.GetProcessesByName(Resources.FFmpegProcessName);
                foreach (var process in processes)
                {
                    process.Kill();
                    process.WaitForExit();
                }
            }
            finally
            {
                Mutex.ReleaseMutex();
            }
        }

        void EnsureDirectoryExists()
        {
            string directory = Path.GetDirectoryName(FFmpegFilePath) ?? Directory.GetCurrentDirectory();

            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);
        }

        void EnsureFFmpegFileExists()
        {
            if (!File.Exists(FFmpegFilePath))
                UnpackFFmpegExecutable(FFmpegFilePath);
        }

        static void UnpackFFmpegExecutable(string path)
        {
            Stream compressedFFmpegStream = Assembly.GetExecutingAssembly()
                                                    .GetManifestResourceStream(Resources.FFmpegManifestResourceName);

            if (compressedFFmpegStream == null)
            {
                throw new Exception(Resources.ExceptionsNullFFmpegGzipStream);
            }

            using (var fileStream = new FileStream(path, FileMode.Create))
            using (var compressedStream = new GZipStream(compressedFFmpegStream, CompressionMode.Decompress))
            {
                compressedStream.CopyTo(fileStream);
            }
        }

        public virtual void Dispose() => Dispose(true);

        void Dispose(bool disposing)
        {
            if (!disposing || isDisposed)
            {
                return;
            }

            FFmpegProcess?.Dispose();

            isDisposed = true;
        }
    }
}
