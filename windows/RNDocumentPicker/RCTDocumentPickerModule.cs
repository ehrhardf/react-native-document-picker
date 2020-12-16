using Microsoft.ReactNative.Managed;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Collections.Generic;
using Windows.ApplicationModel.Core;
using Windows.UI.Core;
using Windows.Storage;
using Windows.Storage.Pickers;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.IO;
using Microsoft.ReactNative;

namespace RNDocumentPicker
{
    [ReactModule]
    class RCTDocumentPickerModule
    {
        private FileOpenPicker _pendingPicker;
        private bool _isInForeground;

        private static readonly String E_FAILED_TO_SHOW_PICKER = "FAILED_TO_SHOW_PICKER";
        private static readonly String E_DOCUMENT_PICKER_CANCELED = "DOCUMENT_PICKER_CANCELED";
        private static readonly String E_UNEXPECTED_EXCEPTION = "UNEXPECTED_EXCEPTION";

        private static readonly String OPTION_TYPE = "type";
        private static readonly String CACHE_TYPE = "cache";
        private static readonly String OPTION_MULIPLE = "multiple";
        private static readonly String OPTION_READ_CONTENT = "readContent";
        private static readonly String FIELD_URI = "uri";
        private static readonly String FIELD_FILE_COPY_URI = "fileCopyUri";
        private static readonly String FIELD_NAME = "name";
        private static readonly String FIELD_TYPE = "type";
        private static readonly String FIELD_SIZE = "size";
        private static readonly String FIELD_CONTENT = "content";

        [ReactInitializer]
        public void Initialize(ReactContext reactContext)
        {
            // Here we should probably be listening to lifecycle events to know the
            // state of the application and respond to OnSuspend and OnResume events.
            // Something like: 
            // reactContext.AddLifecycleEventListener(this);
            // It looks like this may not be supported yet:
            // https://microsoft.github.io/react-native-windows/docs/reactnativehost-api
            _isInForeground = true;
        }

        //public void OnSuspend()
        //{
        //    _isInForeground = false;
        //}

        //public void OnResume()
        //{
        //    _isInForeground = true;
        //    // TODO/ question: shouldn't we resume _pendingPicker here?
        //}

        //public void OnDestroy()
        //{
        //}

        [ReactMethod("pick")]
        public void Pick(JSValue options, ReactPromise<JSValue> promise)
        {
            var optionsObj = options.AsObject();
            try
            {
                FileOpenPicker openPicker = new FileOpenPicker();
                openPicker.ViewMode = PickerViewMode.Thumbnail;
                openPicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
                // Get file type array options
                var fileTypeArray = optionsObj[OPTION_TYPE].AsArray();
                var cache = optionsObj.ContainsKey(CACHE_TYPE) && optionsObj[CACHE_TYPE].AsBoolean();
                // Init file type filter
                if (fileTypeArray != null && fileTypeArray.Count > 0)
                {
                    foreach (JSValue typeString in fileTypeArray)
                    {
                        List<String> types = typeString.AsString().Split(' ').ToList();
                        foreach (String type in types)
                        {
                            if (Regex.Match(type, "(^[.]+[A-Za-z0-9]*$)|(^[*]$)").Success)
                            {
                                openPicker.FileTypeFilter.Add(type);
                            }
                        }
                    }
                }
                else
                {
                    openPicker.FileTypeFilter.Add("*");
                }

                RunOnDispatcher(async () =>
                {
                    try
                    {
                        if (_isInForeground)
                        {
                            var isMultiple = optionsObj.ContainsKey(OPTION_MULIPLE) && optionsObj[OPTION_MULIPLE].AsBoolean();
                            var readContent = optionsObj.ContainsKey(OPTION_READ_CONTENT) && optionsObj[OPTION_READ_CONTENT].AsBoolean();
                            if (isMultiple)
                            {
                                await PickMultipleFileAsync(openPicker, cache, readContent, promise);
                            }
                            else
                            {
                                await PickSingleFileAsync(openPicker, cache, readContent, promise);
                            }
                        }
                        else
                        {
                            _pendingPicker = openPicker;
                        }
                    }
                    catch (Exception ex)
                    {
                        var error = new ReactError() { Code = E_FAILED_TO_SHOW_PICKER, Exception = ex, Message = ex.Message };
                        promise.Reject(error);
                    }
                });
            }
            catch (Exception ex)
            {
                var error = new ReactError() { Code = E_UNEXPECTED_EXCEPTION, Exception = ex, Message = ex.Message };
                promise.Reject(error);
            }
        }

        private async Task<JSValueObject> PrepareFile(StorageFile file, Boolean cache, Boolean readContent)
        {
            String base64Content = null;
            if (readContent)
            {
                var fileStream = await file.OpenReadAsync();
                using (StreamReader reader = new StreamReader(fileStream.AsStream()))
                {
                    using (var memstream = new MemoryStream())
                    {
                        reader.BaseStream.CopyTo(memstream);
                        var bytes = memstream.ToArray();
                        base64Content = Convert.ToBase64String(bytes);
                    }
                }
            }

            if (cache == true)
            {
                var fileInCache = await file.CopyAsync(ApplicationData.Current.TemporaryFolder, file.Name.ToString(), NameCollisionOption.ReplaceExisting).AsTask().ConfigureAwait(false);
                var basicProperties = await fileInCache.GetBasicPropertiesAsync();

                return new JSValueObject {
                    { FIELD_URI, fileInCache.Path },
                    { FIELD_FILE_COPY_URI, fileInCache.Path },
                    { FIELD_TYPE, fileInCache.ContentType },
                    { FIELD_NAME, fileInCache.Name },
                    { FIELD_SIZE, basicProperties.Size},
                    { FIELD_CONTENT, base64Content }
                };
            }
            else
            {
                var basicProperties = await file.GetBasicPropertiesAsync();

                return new JSValueObject {
                    { FIELD_URI, file.Path },
                    { FIELD_FILE_COPY_URI, file.Path },
                    { FIELD_TYPE, file.ContentType },
                    { FIELD_NAME, file.Name },
                    { FIELD_SIZE, basicProperties.Size},
                    { FIELD_CONTENT, base64Content }
                };
            }
        }

        private async Task<bool> PickMultipleFileAsync(FileOpenPicker picker, Boolean cache, Boolean readContent, ReactPromise<JSValue> promise)
        {
            IReadOnlyList<StorageFile> files = await picker.PickMultipleFilesAsync().AsTask().ConfigureAwait(false);
            if (files.Count > 0)
            {
                JSValueArray jarrayObj = new JSValueArray();
                foreach (var file in files)
                {
                    jarrayObj.Add(PrepareFile(file, cache, readContent).Result);
                }
                promise.Resolve(jarrayObj);
            }
            else
            {
                var error = new ReactError() { Code = E_DOCUMENT_PICKER_CANCELED, Message = "User canceled document picker" };
                promise.Reject(error);
            }

            return true;
        }

        private async Task<bool> PickSingleFileAsync(FileOpenPicker picker, Boolean cache, Boolean readContent, ReactPromise<JSValue> promise)
        {
            var file = await picker.PickSingleFileAsync().AsTask().ConfigureAwait(false);
            if (file != null)
            {
                JSValueArray jarrayObj = new JSValueArray
                {
                    PrepareFile(file, cache, readContent).Result
                };
                promise.Resolve(jarrayObj);
            }
            else
            {
                var error = new ReactError() { Code = E_DOCUMENT_PICKER_CANCELED, Message = "User canceled document picker" };
                promise.Reject(error);
            }

            return true;
        }

        //private void OnInvoked(Object error, Object success, ICallback callback)
        //{
        //    callback.Invoke(error, success);
        //}

        private static async void RunOnDispatcher(DispatchedHandler action)
        {
            await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, action).AsTask().ConfigureAwait(false);
        }
    }
}