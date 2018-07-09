using System;
using System.ComponentModel.Design;
using System.IO;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;
using Newtonsoft.Json;
using System.Runtime.InteropServices;

namespace RunCustomGulpFile
{
    /// <summary>
    /// Command handler
    /// </summary>
    internal sealed class RunCustomGulpFileCommand
    {
        /// <summary>
        /// Command ID.
        /// </summary>
        public const int CommandId = 0x0100;

        /// <summary>
        /// Command menu group (command set GUID).
        /// </summary>
        public static readonly Guid CommandSet = new Guid("a95be761-39a8-4739-9253-2ce245e84d28");

        /// <summary>
        /// VS Package that provides this command, not null.
        /// </summary>
        private readonly AsyncPackage package;

        /// <summary>
        /// Initializes a new instance of the <see cref="RunCustomGulpFileCommand"/> class.
        /// Adds our command handlers for menu (commands must exist in the command table file)
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        /// <param name="commandService">Command service to add command to, not null.</param>
        private RunCustomGulpFileCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            this.package = package ?? throw new ArgumentNullException(nameof(package));
            commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

            var menuCommandID = new CommandID(CommandSet, CommandId);
            var menuItem = new MenuCommand(this.Execute, menuCommandID);
            commandService.AddCommand(menuItem);
        }

        /// <summary>
        /// Gets the instance of the command.
        /// </summary>
        public static RunCustomGulpFileCommand Instance
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the service provider from the owner package.
        /// </summary>
        private Microsoft.VisualStudio.Shell.IAsyncServiceProvider ServiceProvider
        {
            get
            {
                return this.package;
            }
        }

        public static DTE2 _dte = null;

        /// <summary>
        /// Initializes the singleton instance of the command.
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        public static async Task InitializeAsync(AsyncPackage package)
        {
            // Verify the current thread is the UI thread - the call to AddCommand in RunCustomGulpFileCommand's constructor requires
            // the UI thread.
            ThreadHelper.ThrowIfNotOnUIThread();

            _dte = await package.GetServiceAsync(typeof(DTE)) as DTE2;

            OleMenuCommandService commandService = await package.GetServiceAsync((typeof(IMenuCommandService))) as OleMenuCommandService;
            Instance = new RunCustomGulpFileCommand(package, commandService);
        }

        /// <summary>
        /// Assets config file class
        /// </summary>
        public class AssetsConfigFile
        {
            public string CustomAssetDirPath { get; set; }
            public string GulpParameters { get; set; }
        }

        /// <summary>
        /// This function is the callback used to execute the command when the menu item is clicked.
        /// See the constructor to see how the menu item is associated with this function using
        /// OleMenuCommandService service and MenuCommand class.
        /// </summary>
        /// <param name="sender">Event sender.</param>
        /// <param name="e">Event args.</param>
        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            string solutionPath = Path.GetDirectoryName(_dte.Solution.FullName);
            string assetsDir = String.Empty;
            string gulpParameters = String.Empty;

            string configFilePath = Path.Combine(solutionPath, "assetsconfig.json");

            if (File.Exists(configFilePath))
            {
                AssetsConfigFile jsonData = new AssetsConfigFile();

                using (StreamReader r = new StreamReader(configFilePath))
                {
                    string json = r.ReadToEnd();
                    jsonData = JsonConvert.DeserializeObject<AssetsConfigFile>(json);
                }

                if (jsonData.CustomAssetDirPath != null)
                {
                    assetsDir = Path.Combine(solutionPath, jsonData.CustomAssetDirPath);
                    gulpParameters = jsonData.GulpParameters;
                }
            }
            else {
                Array subDirectories = Directory.GetDirectories(solutionPath);
                Regex reg = new Regex(@"([a-z]|[A-Z]).*\/?(Assets)");

                foreach (string dir in subDirectories)
                {
                    if (reg.Match(dir).Success)
                    {
                        assetsDir = dir;
                        break;
                    }
                }
            }
            

            if (String.IsNullOrEmpty(assetsDir))
            {
                VsShellUtilities.ShowMessageBox(
                    this.package,
                    "Can't find any assets folder in solution directory",
                    "Run Custom GulpFile",
                    OLEMSGICON.OLEMSGICON_CRITICAL,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                return;
            }

            string customGulpFilePath = assetsDir + "\\gulpfile.js";

            if (!File.Exists(customGulpFilePath))
            {
                VsShellUtilities.ShowMessageBox(
                    this.package,
                    "Can't find any gulpfile.js in assets folder",
                    "Run Custom GulpFile",
                    OLEMSGICON.OLEMSGICON_CRITICAL,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                return;
            }

            System.Diagnostics.Process proc = new System.Diagnostics.Process();
            proc.StartInfo.WorkingDirectory = assetsDir;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                proc.StartInfo.FileName = "CMD.exe";
                proc.StartInfo.Arguments = "/C gulp " + gulpParameters;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                proc.StartInfo.FileName = "/bin/bash";
                proc.StartInfo.Arguments = "-c gulp " + gulpParameters;
            }

            proc.Start();
        }
    }
}