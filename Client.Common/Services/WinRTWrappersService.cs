namespace Client.Common.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading.Tasks;
    using System.Xml.Serialization;
    using Windows.ApplicationModel.DataTransfer;
    using Windows.Foundation;
    using Windows.Storage;
    using Windows.Storage.Pickers;
    using Helpers;
    using MetroLog;

    public class WinRTWrappersService : IWinRTWrappersService
    {
        #region Static Fields

        private static readonly ILogger Log = LogManagerFactory.DefaultLogManager.GetLogger<WinRTWrappersService>();

        #endregion

        #region Public Methods and Operators

        public async Task<IStorageFile> GetNewStorageFile()
        {
            var fileSavePicker = new FileSavePicker();
            fileSavePicker.FileTypeChoices.Add("MixRadio playlist", new List<string> { ".spls" });
            fileSavePicker.DefaultFileExtension = ".spls";
            fileSavePicker.SuggestedFileName = "playlist.spls";
            fileSavePicker.SettingsIdentifier = "PlaylistPicker";

            return await fileSavePicker.PickSaveFileAsync();
        }

        public async Task<T> LoadFromFile<T>(IStorageFile storageFile) where T : new()
        {
            using (var randomAccessStream = await storageFile.OpenReadAsync())
            using (var source = randomAccessStream.AsStreamForRead())
            using (var buffer = new MemoryStream())
            {
                await source.CopyToAsync(buffer);
                buffer.Position = 0;
                return (T)new XmlSerializer(typeof(T)).Deserialize(buffer);
            }
        }

        public async Task<IStorageFile> OpenStorageFile()
        {
            var fileOpenPicker = new FileOpenPicker();
            fileOpenPicker.FileTypeFilter.Add(".spls");
            fileOpenPicker.ViewMode = PickerViewMode.List;
            return await fileOpenPicker.PickSingleFileAsync();
        }

        public void RegisterMediaControlHandler(IMediaControlHandler mediaControlHandler)
        {
            mediaControlHandler.Register();
        }

        public async Task SaveToFile<T>(IStorageFile storageFile, T @object)
        {
            using (var buffer = new MemoryStream())
            {
                new XmlSerializer(typeof(T)).Serialize(buffer, @object);
                buffer.Position = 0;
                using (var transaction = await storageFile.OpenTransactedWriteAsync())
                {
                    transaction.Stream.Size = 0;
                    using (var target = transaction.Stream.AsStreamForWrite())
                    {
                        await buffer.CopyToAsync(target);
                        await target.FlushAsync();
                        await transaction.CommitAsync();
                    }
                }
            }
        }

        public void RegisterShareRequestHandler(Action<DataRequest> requestHandler)
        {
            if (requestHandler != null)
            {
                var dataTransferManager = DataTransferManager.GetForCurrentView();
                dataTransferManager.DataRequested += (sender, eventArgs) => requestHandler(eventArgs.Request);
            }
        }

        public void ShowShareUI()
        {
            DataTransferManager.ShowShareUI();
        }

        #endregion
    }
}
