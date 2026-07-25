# Keryhe.Messaging.IO

![Keryhe.Messaging.IO](https://img.shields.io/nuget/v/Keryhe.Messaging.io.svg)

An implementation of the IMessageListener and IMessagePublisher interfaces by storing and reading files. Supports xml and json file types.

**How to add the FileSystemListener and FileSystemPublisher**

```csharp
using Keryhe.Messaging.IO.Extensions;

builder.Services.AddFileSystemListener<Message>(builder.Configuration.GetSection("FileSystemListener"));

builder.Services.AddFileSystemPublisher<Message>(builder.Configuration.GetSection("FileSystemPublisher"));
```

**appsettings configuration section**

```json
"FileSystemListener": 
{
    "Sources": {
        "orders": {
            "Folder": "c:\\QueueFolder\\Orders",
            "FileType": "Json",
            "CompletedFolder": "",
            "ErrorFolder": "",
            "Interval": 1
        },
        "audit": {
            "Folder": "c:\\QueueFolder\\Audit",
            "FileType": "Xml",
            "CompletedFolder": "",
            "ErrorFolder": "",
            "Interval": 5
        }
    }
}

"FileSystemPublisher": 
{
    "Destinations": {
        "orders": {
            "Folder": "c:\\QueueFolder\\Orders",
            "FileType": "Json"
        },
        "audit": {
            "Folder": "c:\\QueueFolder\\Audit",
            "FileType": "Xml"
        }
    }
}
```

Each key under **Sources** is the name you pass to `SubscribeAsync(source, handler, token)` — call it once per source to listen to. `UnsubscribeAsync(source, token)` stops listening to that source.

Each key under **Destinations** is the name you pass to `SendAsync(message, name)`.

To use the File System as your transport layer, install the [Keryhe.Messaging.IO](https://www.nuget.org/packages/keryhe.messaging.io) package from NuGet.
