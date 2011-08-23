﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Windows.Input;
using NuGet;
using NuGetPackageExplorer.Types;
using LazyPackageCommand = System.Lazy<NuGetPackageExplorer.Types.IPackageCommand, NuGetPackageExplorer.Types.IPackageCommandMetadata>;

namespace PackageExplorerViewModel {

    public sealed class PackageViewModel : ViewModelBase, IDisposable {
        private readonly IPackage _package;
        private EditablePackageMetadata _packageMetadata;
        private PackageFolder _packageRoot;
        private ICommand _saveCommand, _editCommand, _cancelEditCommand, _applyEditCommand, _viewContentCommand, _saveContentCommand, _addAsAssemblyReferenceCommand;
        private ICommand _addContentFolderCommand, _addContentFileCommand, _addNewFolderCommand, _openWithContentFileCommand, _executePackageCommand, _viewPackageAnalysisCommand;
        private RelayCommand<object> _openContentFileCommand, _deleteContentCommand, _renameContentCommand;
        private RelayCommand _publishCommand, _exportCommand;
        private readonly IMruManager _mruManager;
        private readonly IUIServices _uiServices;
        private readonly IPackageEditorService _editorService;
        private readonly ISettingsManager _settingsManager;
        private readonly IList<Lazy<IPackageContentViewer, IPackageContentViewerMetadata>> _contentViewerMetadata;
        private readonly IList<Lazy<IPackageRule>> _packageRules;

        internal PackageViewModel(
            IPackage package,
            string source,
            IMruManager mruManager,
            IUIServices uiServices,
            IPackageEditorService editorService,
            ISettingsManager settingsManager,
            IList<Lazy<IPackageContentViewer, IPackageContentViewerMetadata>> contentViewerMetadata,
            IList<Lazy<IPackageRule>> packageRules) {

            if (package == null) {
                throw new ArgumentNullException("package");
            }
            if (mruManager == null) {
                throw new ArgumentNullException("mruManager");
            }
            if (uiServices == null) {
                throw new ArgumentNullException("uiServices");
            }
            if (editorService == null) {
                throw new ArgumentNullException("editorService");
            }
            if (settingsManager == null) {
                throw new ArgumentNullException("settingsManager");
            }

            _settingsManager = settingsManager;
            _editorService = editorService;
            _uiServices = uiServices;
            _mruManager = mruManager;
            _package = package;
            _contentViewerMetadata = contentViewerMetadata;
            _packageRules = packageRules;

            _packageMetadata = new EditablePackageMetadata(_package);
            PackageSource = source;

            _packageRoot = PathToTreeConverter.Convert(_package.GetFiles().ToList(), this);
        }

        internal IList<Lazy<IPackageContentViewer, IPackageContentViewerMetadata>> ContentViewerMetadata {
            get {
                return _contentViewerMetadata;
            }
        }

        internal IUIServices UIServices {
            get {
                return _uiServices;
            }
        }

        private bool _isInEditMode;
        public bool IsInEditMode {
            get {
                return _isInEditMode;
            }
            private set {
                if (_isInEditMode != value) {
                    _isInEditMode = value;
                    OnPropertyChanged("IsInEditMode");
                    PublishCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string WindowTitle {
            get {
                return Resources.Dialog_Title + " - " + _packageMetadata.ToString();
            }
        }

        public EditablePackageMetadata PackageMetadata {
            get {
                return _packageMetadata;
            }
        }

        private bool _showContentViewer;
        public bool ShowContentViewer {
            get { return _showContentViewer; }
            set {
                if (_showContentViewer != value) {
                    _showContentViewer = value;
                    OnPropertyChanged("ShowContentViewer");
                }
            }
        }

        private bool _showPackageAnalysis;
        public bool ShowPackageAnalysis {
            get {
                return _showPackageAnalysis;
            }
            set {
                if (_showPackageAnalysis != value) {
                    _showPackageAnalysis = value;
                    OnPropertyChanged("ShowPackageAnalysis");
                }
            }
        }

        private FileContentInfo _currentFileInfo;
        public FileContentInfo CurrentFileInfo {
            get { return _currentFileInfo; }
            set {
                if (_currentFileInfo != value) {
                    _currentFileInfo = value;
                    OnPropertyChanged("CurrentFileInfo");
                }
            }
        }

        public ICollection<PackagePart> PackageParts {
            get {
                return _packageRoot.Children;
            }
        }

        public bool IsValid {
            get {
                return PackageHelper.IsPackageValid(PackageMetadata, GetFiles());
            }
        }

        private object _selectedItem;
        public object SelectedItem {
            get {
                return _selectedItem;
            }
            set {
                if (_selectedItem != value) {
                    _selectedItem = value;
                    OnPropertyChanged("SelectedItem");
                    ((ViewContentCommand)ViewContentCommand).RaiseCanExecuteChanged();
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        private string _packageSource;
        public string PackageSource {
            get { return _packageSource; }
            set {
                if (_packageSource != value) {
                    _packageSource = value;
                    OnPropertyChanged("PackageSource");
                }
            }
        }

        private bool _hasEdit;
        public bool HasEdit {
            get {
                return _hasEdit;
            }
            set {
                if (_hasEdit != value) {
                    _hasEdit = value;
                    OnPropertyChanged("HasEdit");
                }
            }
        }

        private SortedCollection<PackageIssue> _packageIssues = new SortedCollection<PackageIssue>(PackageIssueComparer.Instance);
        public SortedCollection<PackageIssue> PackageIssues {
            get {
                return _packageIssues;
            }
        }

        private void SetPackageIssues(IEnumerable<PackageIssue> issues) {
            _packageIssues.Clear();
            _packageIssues.AddRange(issues);
        }

        public void ShowFile(FileContentInfo fileInfo) {
            ShowContentViewer = true;
            CurrentFileInfo = fileInfo;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1024:UsePropertiesWhereAppropriate")]
        internal IEnumerable<IPackageFile> GetFiles() {
            return _packageRoot.GetFiles();
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1024:UsePropertiesWhereAppropriate")]
        public Stream GetCurrentPackageStream() {
            string tempFile = Path.GetTempFileName();
            PackageHelper.SavePackage(PackageMetadata, GetFiles(), tempFile, useTempFile: false);
            if (File.Exists(tempFile)) {
                return File.OpenRead(tempFile);
            }
            else {
                return null;
            }
        }

        public void BeginEdit() {
            // raise the property change event here to force the edit form to rebind 
            // all controls, which will erase all error states, if any, left over from the previous edit
            OnPropertyChanged("PackageMetadata");
            IsInEditMode = true;
        }

        public void CancelEdit() {
            PackageMetadata.ResetErrors();
            IsInEditMode = false;
        }

        private void CommitEdit() {
            HasEdit = true;
            PackageMetadata.ResetErrors();
            IsInEditMode = false;
            OnPropertyChanged("WindowTitle");
        }

        internal void OnSaved(string fileName) {
            HasEdit = false;
            _mruManager.NotifyFileAdded(PackageMetadata, fileName, PackageType.LocalPackage);
        }

        internal void NotifyChanges() {
            HasEdit = true;
        }

        public PackageFolder RootFolder {
            get {
                return _packageRoot;
            }
        }

        private void Export(string rootPath) {
            if (rootPath == null) {
                throw new ArgumentNullException("rootPath");
            }

            if (!Directory.Exists(rootPath)) {
                throw new ArgumentException("Specified directory doesn't exist.");
            }

            // export files
            RootFolder.Export(rootPath);

            // export .nuspec file
            ExportManifest(Path.Combine(rootPath, PackageMetadata.ToString() + ".nuspec"));
        }

        internal void ExportManifest(string fullpath) {
            if (File.Exists(fullpath)) {
                bool confirmed = UIServices.Confirm(
                    Resources.ConfirmToReplaceFile_Title,
                    String.Format(CultureInfo.CurrentCulture, Resources.ConfirmToReplaceFile, fullpath));
                if (!confirmed) {
                    return;
                }
            }

            using (Stream fileStream = File.Create(fullpath)) {
                Manifest manifest = Manifest.Create(PackageMetadata);
                manifest.Files = new List<ManifestFile>();
                manifest.Files.AddRange(
                    RootFolder.GetFiles().Select(f => new ManifestFile { Source = f.Path, Target = f.Path }));
                manifest.Save(fileStream);
            }
        }

        internal void NotifyContentDeleted(PackagePart packagePart) {
            // if the deleted file is being shown in the content pane, close the content pane
            if (CurrentFileInfo != null && CurrentFileInfo.File == packagePart) {
                CloseContentViewer();
            }

            NotifyChanges();
        }

        internal void CloseContentViewer() {
            ShowContentViewer = false;
            CurrentFileInfo = null;
        }

        public void AddDraggedAndDroppedFiles(PackageFolder folder, string[] fileNames) {
            if (folder == null) {
                bool? rememberedAnswer = null;

                for (int i = 0; i < fileNames.Length; i++) {
                    string file = fileNames[i];
                    if (File.Exists(file)) {
                        bool movingFile = false;

                        PackageFolder targetFolder;
                        string guessFolderName = FileHelper.GuessFolderNameFromFile(file);

                        if (rememberedAnswer == null) {
                            // ask user if he wants to move file
                            Tuple<bool?, bool> answer = UIServices.ConfirmMoveFile(
                                Path.GetFileName(file),
                                guessFolderName, fileNames.Length - i - 1);

                            if (answer.Item1 == null) {
                                // user presses Cancel
                                break;
                            }

                            movingFile = (bool)answer.Item1;
                            if (answer.Item2) {
                                rememberedAnswer = answer.Item1;
                            }
                        }
                        else {
                            movingFile = (bool)rememberedAnswer;
                        }

                        if (movingFile) {
                            if (RootFolder.ContainsFolder(guessFolderName)) {
                                targetFolder = (PackageFolder)RootFolder[guessFolderName];
                            }
                            else {
                                targetFolder = RootFolder.AddFolder(guessFolderName);
                            }
                        }
                        else {
                            targetFolder = RootFolder;
                        }

                        targetFolder.AddFile(file);
                    }
                    else if (Directory.Exists(file)) {
                        RootFolder.AddPhysicalFolder(file);
                    }
                }
            }
            else {
                foreach (string file in fileNames) {
                    if (File.Exists(file)) {
                        folder.AddFile(file);
                    }
                    else if (Directory.Exists(file)) {
                        folder.AddPhysicalFolder(file);
                    }
                }
            }
        }

        internal bool IsShowingFileContent(PackageFile file) {
            return ShowContentViewer && CurrentFileInfo.File == file;
        }

        internal void ShowFileContent(PackageFile file) {
            ViewContentCommand.Execute(file);
        }

        public void Dispose() {
            RootFolder.Dispose();
        }

        #region AddContentFileCommand

        public ICommand AddContentFileCommand {
            get {
                if (_addContentFileCommand == null) {
                    _addContentFileCommand = new RelayCommand<object>(AddContentFileExecute, AddContentFileCanExecute);
                }

                return _addContentFileCommand;
            }
        }

        private bool AddContentFileCanExecute(object parameter) {
            parameter = parameter ?? SelectedItem;
            return parameter == null || parameter is PackageFolder;
        }

        private void AddContentFileExecute(object parameter) {
            PackageFolder folder = (parameter ?? SelectedItem) as PackageFolder;
            if (folder != null) {
                AddExistingFileToFolder(folder);
            }
            else {
                AddExistingFileToFolder(RootFolder);
            }
        }

        private void AddExistingFileToFolder(PackageFolder folder) {
            string[] selectedFiles;
            bool result = UIServices.OpenMultipleFilesDialog(
                "Select Files",
                "All files (*.*)|*.*",
                out selectedFiles);

            if (result) {
                foreach (string file in selectedFiles) {
                    folder.AddFile(file);
                }
            }
        }

        #endregion

        #region AddContentFolderCommand

        public ICommand AddContentFolderCommand {
            get {
                if (_addContentFolderCommand == null) {
                    _addContentFolderCommand = new RelayCommand<string>(AddContentFolderExecute, AddContentFolderCanExecute);
                }

                return _addContentFolderCommand;
            }
        }

        private bool AddContentFolderCanExecute(string folderName) {
            if (folderName == null) {
                return false;
            }

            return !RootFolder.ContainsFolder(folderName);
        }

        private void AddContentFolderExecute(string folderName) {
            RootFolder.AddFolder(folderName);
        }

        #endregion

        #region AddNewFolderCommand

        public ICommand AddNewFolderCommand {
            get {
                if (_addNewFolderCommand == null) {
                    _addNewFolderCommand = new RelayCommand<object>(AddNewFolderExecute, AddNewFolderCanExecute);
                }

                return _addNewFolderCommand;
            }
        }

        private bool AddNewFolderCanExecute(object parameter) {
            parameter = parameter ?? SelectedItem;
            return parameter == null || parameter is PackageFolder;
        }

        private void AddNewFolderExecute(object parameter) {
            parameter = parameter ?? SelectedItem ?? RootFolder;
            PackageFolder folder = parameter as PackageFolder;
            string folderName = "NewFolder";
            bool result = UIServices.OpenRenameDialog(folderName, "Provide name for the new folder.", out folderName);
            if (result) {
                folder.AddFolder(folderName);
            }
        }

        #endregion

        #region SavePackageCommand

        public ICommand SaveCommand {
            get {
                if (_saveCommand == null) {
                    _saveCommand = new SavePackageCommand(this);
                }
                return _saveCommand;
            }
        }

        #endregion

        #region EditPackageCommand

        public ICommand EditCommand {
            get {
                if (_editCommand == null) {
                    _editCommand = new RelayCommand(EditPackageExecute, EditPackageCanExecute);
                }
                return _editCommand;
            }
        }

        private bool EditPackageCanExecute() {
            return !IsInEditMode;
        }

        private void EditPackageExecute() {
            _editorService.BeginEdit();
            BeginEdit();
        }

        #endregion

        #region ApplyEditCommand

        public ICommand ApplyEditCommand {
            get {
                if (_applyEditCommand == null) {
                    _applyEditCommand = new RelayCommand(() => ApplyEditExecute());
                }

                return _applyEditCommand;
            }
        }

        internal bool ApplyEditExecute() {
            bool valid = _editorService.CommitEdit();
            if (valid) {
                CommitEdit();
            }

            return valid;
        }

        #endregion

        #region CancelEditCommand

        public ICommand CancelEditCommand {
            get {
                if (_cancelEditCommand == null) {
                    _cancelEditCommand = new RelayCommand(CancelEditExecute);
                }

                return _cancelEditCommand;
            }
        }

        private void CancelEditExecute() {
            _editorService.CancelEdit();
            CancelEdit();
        }

        #endregion

        #region DeleteContentCommand

        public RelayCommand<object> DeleteContentCommand {
            get {
                if (_deleteContentCommand == null) {
                    _deleteContentCommand = new RelayCommand<object>(DeleteContentExecute, DeleteContentCanExecute);
                }

                return _deleteContentCommand;
            }
        }

        private bool DeleteContentCanExecute(object parameter) {
            return (parameter ?? SelectedItem) is PackagePart;
        }

        private void DeleteContentExecute(object parameter) {
            var file = (parameter ?? SelectedItem) as PackagePart;
            if (file != null) {
                file.Delete();
            }
        }

        #endregion

        #region RenameContentCommand

        public RelayCommand<object> RenameContentCommand {
            get {
                if (_renameContentCommand == null) {
                    _renameContentCommand = new RelayCommand<object>(RenameContentExecuted, RenameContentCanExecuted);
                }
                return _renameContentCommand;
            }
        }

        private bool RenameContentCanExecuted(object parameter) {
            return (parameter ?? SelectedItem) is PackagePart;
        }

        private void RenameContentExecuted(object parameter) {
            var part = (parameter ?? SelectedItem) as PackagePart;
            if (part != null) {
                string newName;
                bool result = UIServices.OpenRenameDialog(part.Name, "Provide new name for '" + part.Name + "'.", out newName);
                if (result) {
                    part.Rename(newName);
                }
            }
        }

        #endregion

        #region OpenContentFileCommand

        public RelayCommand<object> OpenContentFileCommand {
            get {
                if (_openContentFileCommand == null) {
                    _openContentFileCommand = new RelayCommand<object>(OpenContentFileExecute, OpenContentFileCanExecute);
                }
                return _openContentFileCommand;
            }
        }

        private bool OpenContentFileCanExecute(object parameter) {
            parameter = parameter ?? SelectedItem;
            return parameter is PackageFile;
        }

        private void OpenContentFileExecute(object parameter) {
            parameter = parameter ?? SelectedItem;
            PackageFile file = parameter as PackageFile;
            if (file != null) {
                FileHelper.OpenFileInShell(file, UIServices);
            }
        }

        #endregion

        #region OpenWithContentFileCommand

        public ICommand OpenWithContentFileCommand {
            get {
                if (_openWithContentFileCommand == null) {
                    _openWithContentFileCommand = new RelayCommand<PackageFile>(FileHelper.OpenFileInShellWith);
                }
                return _openWithContentFileCommand;
            }
        }

        #endregion

        #region SaveContentCommand

        public ICommand SaveContentCommand {
            get {
                if (_saveContentCommand == null) {
                    _saveContentCommand = new RelayCommand<PackageFile>(SaveContentExecute);
                }
                return _saveContentCommand;
            }
        }

        private void SaveContentExecute(PackageFile file) {
            string selectedFileName;
            string title = "Save " + file.Name;
            string filter = "All files (*.*)|*.*";
            int filterIndex;
            if (UIServices.OpenSaveFileDialog(title, file.Name, filter, /* overwritePrompt */ true, out selectedFileName, out filterIndex)) {
                using (FileStream fileStream = File.OpenWrite(selectedFileName)) {
                    file.GetStream().CopyTo(fileStream);
                }
            }
        }

        #endregion

        #region ViewContentCommand

        public ICommand ViewContentCommand {
            get {
                if (_viewContentCommand == null) {
                    _viewContentCommand = new ViewContentCommand(this);
                }
                return _viewContentCommand;
            }
        }

        #endregion

        #region PublishCommand

        public RelayCommand PublishCommand {
            get {
                if (_publishCommand == null) {
                    _publishCommand = new RelayCommand(PublishExecute, PublishCanExecute);
                }
                return _publishCommand;
            }
        }

        private void PublishExecute() {
            if (!NetworkInterface.GetIsNetworkAvailable()) {
                UIServices.Show(Resources.NoNetworkConnection, MessageLevel.Warning);
                return;
            }

            if (!this.IsValid) {
                UIServices.Show(Resources.PackageHasNoFile, MessageLevel.Warning);
                return;
            }

            using (var mruSourceManager = new MruPackageSourceManager(
                        new PublishSourceSettings(_settingsManager))) {
                var publishPackageViewModel = new PublishPackageViewModel(
                    mruSourceManager,
                    _settingsManager,
                    this);
                _uiServices.OpenPublishDialog(publishPackageViewModel);
            }
        }

        private bool PublishCanExecute() {
            return !IsInEditMode;
        }

        #endregion

        #region ExportCommand

        public RelayCommand ExportCommand {
            get {
                if (_exportCommand == null) {
                    _exportCommand = new RelayCommand(ExportExecute, ExportCanExecute);
                }
                return _exportCommand;
            }
        }

        private string _folderPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
        private void ExportExecute() {
            string rootPath;
            if (_uiServices.OpenFolderDialog("Choose a folder to export package to:", _folderPath, out rootPath)) {
                try {
                    Export(rootPath);
                    UIServices.Show(Resources.ExportPackageSuccess, MessageLevel.Information);
                }
                catch (Exception ex) {
                    UIServices.Show(ex.Message, MessageLevel.Error);
                }

                _folderPath = rootPath;
            }
        }

        private bool ExportCanExecute() {
            return !IsInEditMode;
        }

        #endregion

        #region ExecutePackageCommand

        public ICommand ExecutePackageCommand {
            get {
                if (_executePackageCommand == null) {
                    _executePackageCommand = new RelayCommand<LazyPackageCommand>(PackageCommandExecute);
                }
                return _executePackageCommand;
            }
        }

        private void PackageCommandExecute(LazyPackageCommand packageCommand) {
            var package = PackageHelper.BuildPackage(PackageMetadata, GetFiles());
            packageCommand.Value.Execute(package);
        }
        
        #endregion

        #region ViewPackageAnalysisCommand

        public ICommand ViewPackageAnalysisCommand {
            get {
                if (_viewPackageAnalysisCommand == null) {
                    _viewPackageAnalysisCommand = new RelayCommand<string>(ViewPackageAnalysisExecute, CanExecutePackageAnalysis);
                }
                return _viewPackageAnalysisCommand;
            }
        }

        private void ViewPackageAnalysisExecute(string parameter) {
            if (parameter == "Hide") {
                ShowPackageAnalysis = false;
            }
            else if (_packageRules != null) {
                var package = PackageHelper.BuildPackage(PackageMetadata, GetFiles());
                IEnumerable<PackageIssue> allIssues = _packageRules.SelectMany(r => r.Value.Check(package));
                SetPackageIssues(allIssues);
                ShowPackageAnalysis = true;
            }
        }

        private bool CanExecutePackageAnalysis(string parameter) {
            return parameter == "Hide" || !IsInEditMode;
        }

        #endregion

        #region AddAsAssemblyReferenceCommand

        public ICommand AddAsAssemblyReferenceCommand {
            get {
                if (_addAsAssemblyReferenceCommand == null) {
                    _addAsAssemblyReferenceCommand = new RelayCommand<PackageFile>(AddAsAssemblyReferenceExecute, CanAddAsAssemblyReference);
                }
                return _addAsAssemblyReferenceCommand;
            }
        }

        private bool CanAddAsAssemblyReference(PackageFile file) {
            if (file == null) {
                return false;
            }

            return file != null && 
                   (file.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                    file.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) &&
                    (IsInEditMode || !PackageMetadata.ContainsAssemblyReference(file.Name));
        }

        private void AddAsAssemblyReferenceExecute(PackageFile file) {
            if (file == null) {
                return;
            }

            if (IsInEditMode) {
                _editorService.AddAssemblyReference(file.Name);
            }
            else {
                PackageMetadata.AddAssemblyReference(file.Name);
            }
        }

        #endregion
    }
}