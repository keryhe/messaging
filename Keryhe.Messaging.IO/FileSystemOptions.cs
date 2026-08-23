using System;
using System.Collections.Generic;
using System.Text;

namespace Keryhe.Messaging.IO
{
    public class FileSystemListenerOptions
    {
        public Dictionary<string, FileSystemListenerSourceOptions> Sources { get; set; }
    }

    public class FileSystemListenerSourceOptions
    {
        public FileSystemListenerSourceOptions()
        {
            // Left at the int default of 0, Task.Delay(TimeSpan.Zero) is a completed task, so the
            // scan loop never throttles at all — the same failure as S7 and G5, on a third provider.
            Interval = 5;
        }

        public string Folder { get; set; }
        public string FileType { get; set; }
        public string CompletedFolder { get; set; }
        public string ErrorFolder { get; set; }
        public int Interval { get; set; }
    }

    public class FileSystemPublisherOptions
    {
        public Dictionary<string, FileSystemDestinationOptions> Destinations { get; set; }
    }

    public class FileSystemDestinationOptions
    {
        public string Folder { get; set; }
        public string FileType { get; set; }
    }
}
