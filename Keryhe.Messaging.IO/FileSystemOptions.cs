using System;
using System.Collections.Generic;
using System.Text;

namespace Keryhe.Messaging.IO
{
    public class FileSystemOptions
    {
        public string Folder { get; set; }    
        public string FileType { get; set; }      
    }

    public class FileSystemListenerOptions : FileSystemOptions
    {
        public string CompletedFolder { get; set; }
        public int Interval { get; set; }
    }

    public class FileSystemPublisherOptions : FileSystemOptions
    {
    }
}
