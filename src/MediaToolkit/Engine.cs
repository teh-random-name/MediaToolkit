using MediaToolkit.Model;
using MediaToolkit.Options;
using MediaToolkit.Properties;
using MediaToolkit.Util;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace MediaToolkit
{
    public class Engine : EngineBase
    {
        public event EventHandler<ConversionCompleteEventArgs> ConversionCompleteEvent;
        public event EventHandler<ConvertProgressEventArgs> ConvertProgressEvent;

        public Engine() { }

        public Engine(string ffMpegPath)
            : base(ffMpegPath) { }

        public void Convert(MediaFile inputFile, MediaFile outputFile, ConversionOptions options)
        {
            var engineParams = new EngineParameters
            {
                InputFile = inputFile,
                OutputFile = outputFile,
                ConversionOptions = options,
                Task = FFmpegTask.Convert
            };

            FFmpegEngine(engineParams);
        }

        public void Convert(MediaFile inputFile, MediaFile outputFile)
        {
            var engineParams = new EngineParameters
            {
                InputFile = inputFile,
                OutputFile = outputFile,
                Task = FFmpegTask.Convert
            };

            FFmpegEngine(engineParams);
        }

        public void CustomCommand(string ffmpegCommand)
        {
            if (string.IsNullOrWhiteSpace(ffmpegCommand))
            {
                throw new ArgumentNullException($"{nameof(ffmpegCommand)} cannot be null.");
            }

            var engineParameters = new EngineParameters { CustomArguments = ffmpegCommand };

            StartFFmpegProcess(engineParameters);
        }

        public void GetMetadata(MediaFile inputFile)
        {
            var engineParams = new EngineParameters
            {
                InputFile = inputFile,
                Task = FFmpegTask.GetMetaData
            };

            FFmpegEngine(engineParams);
        }

        public void GetThumbnail(MediaFile inputFile, MediaFile outputFile, ConversionOptions options)
        {
            var engineParams = new EngineParameters
            {
                InputFile = inputFile,
                OutputFile = outputFile,
                ConversionOptions = options,
                Task = FFmpegTask.GetThumbnail
            };

            FFmpegEngine(engineParams);
        }

        void FFmpegEngine(EngineParameters engineParameters)
        {
            if (!engineParameters.InputFile.Filename.StartsWith("http://") && !File.Exists(engineParameters.InputFile.Filename))
            {
                throw new FileNotFoundException(Resources.ExceptionMediaInputFileNotFound, engineParameters.InputFile.Filename);
            }

            try
            {
                Mutex.WaitOne();
                StartFFmpegProcess(engineParameters);
            }
            finally
            {
                Mutex.ReleaseMutex();
            }
        }

        ProcessStartInfo GenerateStartInfo(EngineParameters engineParameters)
        {
            string arguments = CommandBuilder.Serialize(engineParameters);

            return GenerateStartInfo(arguments);
        }

        ProcessStartInfo GenerateStartInfo(string arguments)
        {
            //windows case
            if (Path.DirectorySeparatorChar == '\\')
            {
                return new ProcessStartInfo
                {
                    Arguments = "-nostdin -y -loglevel info " + arguments,
                    FileName = FFmpegFilePath,
                    CreateNoWindow = true,
                    RedirectStandardInput = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
            }
            else //linux case: -nostdin options doesn't exist at least in debian ffmpeg
            {
                return new ProcessStartInfo
                {
                    Arguments = "-y -loglevel info " + arguments,
                    FileName = FFmpegFilePath,
                    CreateNoWindow = true,
                    RedirectStandardInput = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
            }
        }

        void OnConversionComplete(ConversionCompleteEventArgs e) => ConversionCompleteEvent?.Invoke(this, e);

        void OnProgressChanged(ConvertProgressEventArgs e) => ConvertProgressEvent?.Invoke(this, e);

        void StartFFmpegProcess(EngineParameters engineParameters)
        {
            var receivedMessagesLog = new List<string>();
            var totalMediaDuration = new TimeSpan();

            ProcessStartInfo processStartInfo = engineParameters.HasCustomArguments
                                              ? GenerateStartInfo(engineParameters.CustomArguments)
                                              : GenerateStartInfo(engineParameters);

            using (FFmpegProcess = Process.Start(processStartInfo))
            {
                Exception caughtException = null;
                if (FFmpegProcess == null)
                {
                    throw new InvalidOperationException(Resources.ExceptionsFFmpegProcessNotRunning);
                }

                FFmpegProcess.ErrorDataReceived += (sender, received) =>
                {
                    if (received.Data == null)
                    {
                        return;
                    }

                    try
                    {

                        receivedMessagesLog.Insert(0, received.Data);
                        if (engineParameters.InputFile != null)
                        {
                            RegexEngine.TestVideo(received.Data, engineParameters);
                            RegexEngine.TestAudio(received.Data, engineParameters);

                            Match matchDuration = RegexEngine.Index[RegexEngine.Find.Duration].Match(received.Data);
                            if (matchDuration.Success)
                            {
                                if (engineParameters.InputFile.Metadata == null)
                                {
                                    engineParameters.InputFile.Metadata = new Metadata();
                                }

                                TimeSpan.TryParse(matchDuration.Groups[1].Value, out totalMediaDuration);
                                engineParameters.InputFile.Metadata.Duration = totalMediaDuration;
                            }
                        }

                        if (RegexEngine.IsProgressData(received.Data, out ConvertProgressEventArgs progressEvent))
                        {
                            progressEvent.TotalDuration = totalMediaDuration;
                            OnProgressChanged(progressEvent);
                        }
                        else if (RegexEngine.IsConvertCompleteData(received.Data, out ConversionCompleteEventArgs convertCompleteEvent))
                        {
                            convertCompleteEvent.TotalDuration = totalMediaDuration;
                            OnConversionComplete(convertCompleteEvent);
                        }
                    }
                    catch (Exception ex)
                    {
                        // catch the exception and kill the process since we're in a faulted state
                        caughtException = ex;

                        try
                        {
                            FFmpegProcess.Kill();
                        }
                        catch (InvalidOperationException)
                        {
                            // swallow exceptions that are thrown when killing the process, 
                            // one possible candidate is the application ending naturally before we get a chance to kill it
                        }
                    }
                };

                FFmpegProcess.BeginErrorReadLine();
                FFmpegProcess.WaitForExit();

                if ((FFmpegProcess.ExitCode != 0 && FFmpegProcess.ExitCode != 1) || caughtException != null)
                {
                    throw new Exception(
                        FFmpegProcess.ExitCode + ": " + receivedMessagesLog[1] + receivedMessagesLog[0],
                        caughtException);
                }
            }
        }
    }
}