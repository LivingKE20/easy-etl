﻿using Library.Infra;
using System.Diagnostics;
using JsonStreamer;

namespace Library.Extractors.Json
{
    public class JsonDataExtractor : IDataExtractor
    {
        public event ReadNotification? OnRead;
        public event ReadNotification? OnFinish;
        public event EasyEtlErrorEventHandler? OnError;

        private readonly JsonDataExtractorConfig _config;
        private readonly Stopwatch _timer = new();

        public long TotalLines { get; set; }
        public int LineNumber { get; set; }
        public long BytesRead { get; set; }
        public double PercentRead { get; set; }
        public long FileSize { get; set; }
        public double Speed { get; set; }

        /// <summary>
        /// Initializes a new instance of the JsonDataExtractor with the specified configuration.
        /// </summary>
        /// <param name="config">The configuration for the data extraction.</param>
        public JsonDataExtractor(JsonDataExtractorConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            var fileInfo = new FileInfo(_config.FilePath);
            if (!fileInfo.Exists)
                throw new FileNotFoundException("The input file was not found.", _config.FilePath);

            FileSize = fileInfo.Length;
            TotalLines = FileStreamHelper.CountLinesParallel(_config.FilePath);
        }

        /// <summary>
        /// Extracts data from the specified JSON or JSONL file.
        /// </summary>
        /// <param name="processRow">The action to process each row of extracted data.</param>
        public void Extract(RowAction processRow)
        {
            try
            {
                _timer.Start();

                using var fs = new FileStream(_config.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);

                Task.Run(async () =>
                {
                    await foreach (var rowData in fs.ReadStreamingAsync<Dictionary<string, object?>>())
                    {
                        BytesRead = fs.Position;
                        var buffer = rowData;
                        ProcessLine(ref buffer, processRow);
                    }
                }).Wait();


                //var serializer = Newtonsoft.Json.JsonSerializer.CreateDefault();
                //Dictionary<string, object?> rowData;

                //using (var s = File.Open(_config.FilePath, FileMode.Open))
                //using (var sr = new StreamReader(s))
                //using (var reader = new JsonTextReader(sr))
                //{
                //    reader.SupportMultipleContent = _config.IsJsonl;
                //    while (reader.Read())
                //    {
                //        BytesRead += Encoding.Unicode.GetByteCount(reader.Value?.ToString() ?? string.Empty);

                //        try
                //        {
                //            rowData = serializer.Deserialize<Dictionary<string, object?>>(reader) ?? [];
                //            ProcessLine(ref rowData, processRow);
                //        }
                //        catch (JsonReaderException jEx)
                //        {
                //            OnError?.Invoke(new ErrorNotificationEventArgs(EtlType.Extract, jEx, new Dictionary<string, object?>(), LineNumber));
                //            break;
                //        }

                //    }
                //}

                NotifyFinish();
            }
            catch (Exception ex)
            {
                OnError?.Invoke(new ErrorNotificationEventArgs(EtlType.Extract, ex, new Dictionary<string, object?>(), LineNumber));
                throw;
            }
            finally
            {
                _timer.Stop();
            }
        }



        /// <summary>
        /// Processes a single line of JSONL data and notifies about the read progress.
        /// </summary>
        /// <param name="line">The line of JSONL to process.</param>
        /// <param name="processRow">The action to process the row.</param>
        private void ProcessLine(ref Dictionary<string, object?> rowData, RowAction processRow)
        {
            processRow(ref rowData);
            LineNumber++;
            NotifyReadProgress();
        }

        /// <summary>
        /// Notifies subscribers of the progress of data reading.
        /// </summary>
        private void NotifyReadProgress()
        {
            //if (LineNumber % _config.NotifyAfter == 0)
            //{
            PercentRead = (double)BytesRead / FileSize * 100;
            Speed = LineNumber / _timer.Elapsed.TotalSeconds;
            OnRead?.Invoke(new ExtractNotificationEventArgs(TotalLines, LineNumber, FileSize, BytesRead, PercentRead, Speed));
            //}
        }

        /// <summary>
        /// Notifies subscribers that the data extraction process has completed.
        /// </summary>
        private void NotifyFinish()
        {
            TotalLines = LineNumber;
            PercentRead = 100;
            Speed = LineNumber / _timer.Elapsed.TotalSeconds;
            OnFinish?.Invoke(new ExtractNotificationEventArgs(TotalLines, LineNumber, FileSize, BytesRead, PercentRead, Speed));
        }
    }
}
