﻿// PROJECT : MsCrmTools.WebResourcesManager
// This project was developed by Tanguy Touzard
// CODEPLEX: http://xrmtoolbox.codeplex.com
// BLOG: http://mscrmtools.blogspot.com

using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using MsCrmTools.WebResourcesManager.AppCode;
using MsCrmTools.WebResourcesManager.AppCode.EventHandlers;
using MsCrmTools.WebResourcesManager.DelegatesHelpers;
using MsCrmTools.WebResourcesManager.Forms;
using MsCrmTools.WebResourcesManager.Forms.Solutions;
using MsCrmTools.WebResourcesManager.New.EventHandlers;
using MsCrmTools.WebResourcesManager.Properties;
using MsCrmTools.WebResourcesManager.UserControls;
using System;
using System.Activities.Presentation.Metadata;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Crm.Sdk.Messages;
using XrmToolBox.Extensibility;
using XrmToolBox.Extensibility.Interfaces;

namespace MsCrmTools.WebResourcesManager
{
    public partial class WebResourcesManager : PluginControlBase, IGitHubPlugin, IHelpPlugin
    {
        #region Variables

        private const string OpenfileTitleMask = "Select the {0} to replace the existing web resource";

        private string currentFolderForFiles;

        private EventManager evtManager;

        /// <summary>
        /// Scripts Manager
        /// </summary>
        private AppCode.WebResourceManager wrManager;

        public string RepositoryName
        {
            get
            {
                return "MsCrmTools.WebResourcesManager";
            }
        }

        public string UserName
        {
            get
            {
                return "MscrmTools";
            }
        }

        public string HelpUrl
        {
            get
            {
                return "https://github.com/MscrmTools/MsCrmTools.WebResourcesManager/wiki";
            }
        }

        #endregion Variables

        #region Constructor

        public WebResourcesManager()
        {
            InitializeComponent();

            evtManager = new EventManager(Options.Instance);

            toolStripScriptContent.Visible = false;
        }

        #endregion Constructor

        #region Methods

        #region CRM - Load web resources

        public void LoadWebResourceFromASpecificSolution()
        {
            LoadWebResourcesGeneral(true);
        }

        public void LoadWebResourcesGeneral(bool fromSolution)
        {
            Guid solutionId = Guid.Empty;
            List<int> typesToload = new List<int>();
            bool loadAllWebresources = false;
            bool hideMicrosoftWebresources = false;

            // If from solution, display the solution picker so that user can
            // select the solution containing the web resources he wants to
            // display
            if (fromSolution)
            {
                var sPicker = new SolutionPicker(Service) { StartPosition = FormStartPosition.CenterParent };
                if (sPicker.ShowDialog(ParentForm) == DialogResult.OK)
                {
                    solutionId = sPicker.SelectedSolution.Id;
                    loadAllWebresources = sPicker.LoadAllWebresources;
                }
                else
                {
                    return;
                }
            }

            if (!loadAllWebresources)
            {
                // Display web resource types selection dialog
                var dialog = new WebResourceTypeSelectorDialog(fromSolution, ConnectionDetail.OrganizationMajorVersion);
                if (dialog.ShowDialog(ParentForm) == DialogResult.OK)
                {
                    typesToload = dialog.TypesToLoad;
                    hideMicrosoftWebresources = dialog.HideMicrosoftWebresources;
                }
                else
                {
                    return;
                }
            }

            webresourceTreeView1.Enabled = false;
            webresourceTreeView1.Service = Service;
            tabOpenedResources.TabPages.Clear();

            WorkAsync(new WorkAsyncInfo("Loading web resources...", e =>
            {
                var args = (Tuple<Guid, List<int>, bool>)e.Argument;

                webresourceTreeView1.LoadWebResourcesFromServer(args.Item1, args.Item2, args.Item3);
            })
            {
                AsyncArgument = new Tuple<Guid, List<int>, bool>(solutionId, typesToload, hideMicrosoftWebresources),
                PostWorkCallBack = e =>
                {
                    webresourceTreeView1.DisplayWebResources(Options.Instance.ExpandAllOnLoadingResources);

                    webresourceTreeView1.Enabled = true;
                }
            });
        }

        private void TsmiLoadWebResourcesClick(object sender, EventArgs e)
        {
            ExecuteMethod(LoadWebResourcesGeneral, false);
        }

        private void TsmiLoadWebResourcesFromASpecificSolutionClick(object sender, EventArgs e)
        {
            ExecuteMethod(LoadWebResourceFromASpecificSolution);
        }

        #endregion CRM - Load web resources

        #region CRM - Update web resources

        private void DoUpdate(bool[] options)
        {
            try
            {
                var resources = webresourceTreeView1.GetCheckedResources();

                if (resources.Count == 0)
                {
                    MessageBox.Show(this,
                        "Please check at least one web resource before using this function",
                        "Warning",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);

                    return;
                }

                UpdateWebResources(options[0], resources, options[1]);
            }
            catch (Exception error)
            {
                MessageBox.Show(this, "An error occured: " + error.ToString(), "error", MessageBoxButtons.OK,
                                MessageBoxIcon.Error);
            }
        }

        private void DoUpdateWebResources(bool publish, bool addToSolution)
        {
            ExecuteMethod(DoUpdate, new[] { publish, addToSolution });
        }

        private void TsmiSaveAndPublishToCrmServerClick(object sender, EventArgs e)
        {
            if (TreeViewHelper.CheckOnlyThisNode(webresourceTreeView1))
                return;

            DoUpdateWebResources(true, false);
        }

        private void TsmiSavePublishAndAddToSolutionClick(object sender, EventArgs e)
        {
            if (TreeViewHelper.CheckOnlyThisNode(webresourceTreeView1))
                return;

            DoUpdateWebResources(true, true);
        }

        private void TsmiSaveToCrmServerClick(object sender, EventArgs e)
        {
            if (TreeViewHelper.CheckOnlyThisNode(webresourceTreeView1))
                return;

            DoUpdateWebResources(false, false);
        }

        private void TsmiUpdateAndPublishCheckedWebResourcesClick(object sender, EventArgs e)
        {
            DoUpdateWebResources(true, false);
        }

        private void TsmiUpdateCheckedWebResourcesClick(object sender, EventArgs e)
        {
            DoUpdateWebResources(false, false);
        }

        private void TsmiUpdatePublishAndAddToSolutionClick(object sender, EventArgs e)
        {
            DoUpdateWebResources(true, true);
        }

        private void UpdateWebResources(bool publish, IEnumerable<WebResource> webResources, bool addToSolution = false)
        {
            var solutionUniqueName = string.Empty;
            if (addToSolution)
            {
                var sPicker = new SolutionPicker(Service, true) { StartPosition = FormStartPosition.CenterParent };

                if (sPicker.ShowDialog(this) == DialogResult.OK)
                {
                    solutionUniqueName = sPicker.SelectedSolution["uniquename"].ToString();
                }
                else
                {
                    return;
                }
            }

            SetWorkingState(true);
            var parameters = new object[] { webResources, publish, solutionUniqueName };

            WorkAsync(new WorkAsyncInfo("Updating web resources...",
                 (bw, e) =>
                 {
                     var webResourceManager = new AppCode.WebResourceManager(Service);
                     var resourceToPublish = new List<WebResource>();
                     var resources = new List<WebResource>();

                     // Add Regular Resources, and Associated Web Resources
                     foreach (var resource in (IEnumerable<WebResource>)((object[])e.Argument)[0])
                     {
                         resources.Add(resource);
                         resources.AddRange(resource.AssociatedResources);
                     }

                     var wrDifferentFromServer = new List<WebResource>();

                     foreach (var wr in resources)
                     {
                         Entity serverVersion = null;
                         if (wr.Entity != null && wr.Entity.Id != Guid.Empty)
                         {
                             serverVersion = webResourceManager.RetrieveWebResource(wr.Entity.Id);
                         }

                         if (serverVersion != null && serverVersion.GetAttributeValue<string>("content") != wr.OriginalBase64)
                         {
                             wrDifferentFromServer.Add(wr);
                         }
                         else
                         {
                             bw.ReportProgress(1, string.Format("Updating {0}...", wr.Entity.GetAttributeValue<string>("name")));

                             webResourceManager.UpdateWebResource(wr);
                             resourceToPublish.Add(wr);
                         }
                     }

                     if (wrDifferentFromServer.Count > 0)
                     {
                         if (
                             CommonDelegates.DisplayMessageBox(null,
                                 string.Format(
                                     "The following web resources were updated on the server by someone else:\r\n{0}\r\n\r\nAre you sure you want to update them with your content?",
                                     String.Join("\r\n", wrDifferentFromServer.Select(r => r.Entity.GetAttributeValue<string>("name")))),
                                 "Warning", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                         {
                             foreach (var resource in wrDifferentFromServer)
                             {
                                 bw.ReportProgress(1, string.Format("Updating {0}...", resource.Entity.GetAttributeValue<string>("name")));

                                 webResourceManager.UpdateWebResource(resource);
                                 resourceToPublish.Add(resource);
                             }
                         }
                     }

                     // Process post Update command
                     if (!string.IsNullOrEmpty(Options.Instance.AfterUpdateCommand))
                     {
                         foreach (var webResource in resourceToPublish)
                         {
                             evtManager.ActAfterUpdate(webResource);
                         }
                     }

                     // if publish
                     if ((bool)((object[])e.Argument)[1] && wrDifferentFromServer.Count <= resources.Count())
                     {
                         bw.ReportProgress(2, "Publishing web resources...");

                         webResourceManager.PublishWebResources(resourceToPublish);
                     }

                     // Process post Publish command
                     if (!string.IsNullOrEmpty(Options.Instance.AfterPublishCommand))
                     {
                         foreach (var webResource in resourceToPublish)
                         {
                             evtManager.ActAfterPublish(webResource);
                         }
                     }

                     if (((object[])e.Argument)[2].ToString().Length > 0 && wrDifferentFromServer.Count < resources.Count())
                     {
                         bw.ReportProgress(3, "Adding web resources to solution...");

                         webResourceManager.AddToSolution(resourceToPublish, ((object[])e.Argument)[2].ToString());
                     }

                     e.Result = resourceToPublish;
                 })
            {
                AsyncArgument = parameters,
                PostWorkCallBack = e =>
                {
                    if (e.Error != null)
                    {
                        MessageBox.Show(this, e.Error.Message, Resources.MessageBox_ErrorTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }

                    var wrList = (List<WebResource>) e.Result;

                    foreach (TabPage tab in tabOpenedResources.TabPages)
                    {
                        var wr = (WebResource) tab.Tag;

                        if (wrList.Contains(wr))
                        {
                            tab.ForeColor = Color.Black;
                            tab.Text = wr.Entity.GetAttributeValue<string>("name");
                        }

                        if (tab == tabOpenedResources.SelectedTab)
                        {
                            lblWebresourceName.Text = tab.Text;
                            lblWebresourceName.ForeColor = Color.Black;
                        }
                    }
                    
                    SetWorkingState(false);
                },
                ProgressChanged = e => SetWorkingMessage(e.UserState.ToString())
            });
        }

        #endregion CRM - Update web resources

        #region DISK - Load web resources

        private void TsmiLoadWebResourcesFromDiskClick(object sender, EventArgs e)
        {
            try
            {
                // Let the user decides where to find files
                var fbd = new CustomFolderBrowserDialog(true);

                if (string.IsNullOrWhiteSpace(currentFolderForFiles))
                {
                    currentFolderForFiles = Options.Instance.LastFolderUsed;
                }

                if (!string.IsNullOrEmpty(currentFolderForFiles))
                {
                    fbd.FolderPath = currentFolderForFiles;
                }

                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    currentFolderForFiles = fbd.FolderPath;
                    var options = Options.Instance;
                    options.LastFolderUsed = currentFolderForFiles;
                    Options.Instance.Save();

                    tabOpenedResources.TabPages.Clear();

                    var invalidFilenames = webresourceTreeView1.LoadWebResourcesFromDisk(fbd.FolderPath, fbd.ExtensionsToLoad);

                    if (invalidFilenames.Count > 0)
                    {
                        var errorDialog = new InvalidFileListDialog(invalidFilenames)
                        {
                            StartPosition =
                                FormStartPosition.CenterParent
                        };
                        errorDialog.ShowDialog(this);
                    }
                }
            }
            catch (Exception error)
            {
                MessageBox.Show(this, "Error while loading web resources: " + error.Message, Resources.MessageBox_ErrorTitle,
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void TsmiRefreshFromDiskClick(object sender, EventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(webresourceTreeView1.SelectedResource.FilePath))
                {
                    return;
                }

                var invalidFilesList = webresourceTreeView1.RefreshFromDisk();

                if (invalidFilesList.Any())
                {
                    var errorDialog = new InvalidFileListDialog(invalidFilesList)
                    {
                        StartPosition = FormStartPosition.CenterParent
                    };
                    errorDialog.ShowDialog(this);
                }

                if (webresourceTreeView1.SelectedResource.State != WebresourceState.None)
                {
                    string message = "this web resource has been updated since last opening. Are you sure you want to refresh the content from disk?";
                    if (MessageBox.Show(this, message, "Warning", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) ==
                        DialogResult.Yes)
                    {
                        var tab = tabOpenedResources.TabPages.Cast<TabPage>()
                            .FirstOrDefault(t => t.Tag == webresourceTreeView1.SelectedResource);

                        if (tab != null)
                        {
                            webresourceTreeView1.SelectedResource.ReinitStatus();
                            DisplayWebResourceControl(webresourceTreeView1.SelectedResource, true);
                        }

                    }
                }
            }
            catch (Exception error)
            {
                MessageBox.Show(ParentForm,
                    error.Message,
                    Resources.MessageBox_ErrorTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        #endregion DISK - Load web resources

        #region DISK - Save web resources

        private void SaveWebResourcesToDisk(IEnumerable<WebResource> resources, bool withRoot = false)
        {
            var fbd = new CustomFolderBrowserDialog(true, false);

            if (string.IsNullOrWhiteSpace(currentFolderForFiles))
            {
                currentFolderForFiles = Options.Instance.LastFolderUsed;
            }

            if (!string.IsNullOrEmpty(currentFolderForFiles))
            {
                fbd.FolderPath = currentFolderForFiles;
            }

            if (fbd.ShowDialog() == DialogResult.OK)
            {
                currentFolderForFiles = fbd.FolderPath;
                var options = Options.Instance;
                options.LastFolderUsed = currentFolderForFiles;
                Options.Instance.Save();
                foreach (var resource in resources)
                {
                    if (resource.Entity != null)
                    {
                        var resourceEntity = resource.Entity;

                        if (resourceEntity.GetAttributeValue<string>("content").Length > 0)
                        {
                            string[] partPath = resourceEntity.GetAttributeValue<string>("name").Split('/');
                            string path = fbd.FolderPath;

                            if (withRoot)
                            {
                                for (int i = 0; i < partPath.Length - 1; i++)
                                {
                                    path = Path.Combine(path, partPath[i]);

                                    if (!Directory.Exists(path))
                                    {
                                        Directory.CreateDirectory(path);
                                    }
                                }
                            }

                            path = Path.Combine(path, partPath[partPath.Length - 1]);

                            byte[] bytes = Convert.FromBase64String(resourceEntity["content"].ToString());
                            File.WriteAllBytes(path, bytes);

                            resource.FilePath = path;
                        }
                    }
                }
            }
        }

        private void TsmiSaveAllWebResourcesToDiskClick(object sender, EventArgs e)
        {
            try
            {
                SaveWebResourcesToDisk(webresourceTreeView1.GetCheckedResources());
            }
            catch (Exception error)
            {
                MessageBox.Show(this, "Error while saving web resources: " + error.Message, Resources.MessageBox_ErrorTitle,
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void TsmiSaveCheckedWebResourcesToDiskClick(object sender, EventArgs e)
        {
            try
            {
                SaveWebResourcesToDisk(webresourceTreeView1.GetCheckedResources(), true);
            }
            catch (Exception error)
            {
                MessageBox.Show(this, "Error while saving web resources: " + error.Message, Resources.MessageBox_ErrorTitle,
                             MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        #endregion DISK - Save web resources

        #region CRM/DISK - Delete Web resources

        private void DeleteWebResource()
        {
            try
            {
                if (TreeViewHelper.CheckOnlyThisNode(webresourceTreeView1))
                    return;

                TreeNode selectedNode = webresourceTreeView1.SelectedNode;

                if (selectedNode.ImageIndex > 1)
                {
                    if (DialogResult.Yes == MessageBox.Show(this,
                                                            "This web resource will be deleted from the Crm server if you are connected and this web resource exists.\r\nAre you sure you want to delete this web resource?",
                                                            Resources.MessageBox_QuestionTitle,
                                                            MessageBoxButtons.YesNo,
                                                            MessageBoxIcon.Question))
                    {
                        var wr = selectedNode.Tag as WebResource;

                        if (wr != null && wr.Entity != null && wr.Entity.Id != Guid.Empty)
                        {
                            webresourceTreeView1.Service = Service;

                            wrManager = new AppCode.WebResourceManager(Service);

                            WorkAsync(new WorkAsyncInfo("Deleting web resource...", e => wrManager.DeleteWebResource((Entity)e.Argument))
                            {
                                AsyncArgument = wr.Entity,
                                PostWorkCallBack = e =>
                                {
                                    if (e.Error != null)
                                    {
                                        MessageBox.Show(this, "An error occured: " + e.Error, Resources.MessageBox_ErrorTitle,
                                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                                    }
                                    else
                                    {
                                        webresourceTreeView1.RemoveNode(selectedNode);
                                    }

                                    SetWorkingState(false);
                                }
                            });
                        }
                        else
                        {
                            webresourceTreeView1.RemoveNode(selectedNode);
                        }
                    }
                }
                else
                {
                    webresourceTreeView1.RemoveNode(selectedNode);
                }
            }
            catch (Exception error)
            {
                MessageBox.Show(this, "Error while deleting web resource: " + error.Message, Resources.MessageBox_ErrorTitle,
                             MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void TsmiDeleteClick(object sender, EventArgs e)
        {
            ExecuteMethod(DeleteWebResource);
        }

        #endregion CRM/DISK - Delete Web resources

        #region TREEVIEW - Manage content

        private void AddNewEmptyWebRessource(object sender, EventArgs e)
        {
            string extension = string.Empty;

            switch (((ToolStripMenuItem)sender).Name)
            {
                case "hTMLToolStripMenuItem":
                    extension = "html";
                    break;

                case "cSSToolStripMenuItem":
                    extension = "css";
                    break;

                case "scriptToolStripMenuItem":
                    extension = "js";
                    break;

                case "dataToolStripMenuItem":
                    extension = "xml";
                    break;

                case "xSLTToolStripMenuItem":
                    extension = "xslt";
                    break;

                default:
                    {
                        MessageBox.Show(this, "Can't determine web resource type requested!", Resources.MessageBox_ErrorTitle,
                                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    break;
            }

            webresourceTreeView1.CreateEmptyWebResource(extension);
        }

        private void TbsClearTreeClick(object sender, EventArgs e)
        {
            webresourceTreeView1.ClearNodes();
            tabOpenedResources.TabPages.Clear();
            lblWebresourceName.Text = "";
            tslCurrentlyLoadedSolution.Text = "";
            toolStripScriptContent.Visible = false;
            tsbClear.Visible = false;
        }

        private void TsbNewRootClick(object sender, EventArgs e)
        {
            webresourceTreeView1.CreateRoot();
        }

        private void TsmiAddNewFolderClick(object sender, EventArgs e)
        {
            webresourceTreeView1.CreateFolder();
        }

        private void TsmiAddNewWebResourceClick(object sender, EventArgs e)
        {
            webresourceTreeView1.AddExistingWebResource();
        }

        private void TsmiCopyWebResourceNameToClipboardClick(object sender, EventArgs e)
        {
            var name = webresourceTreeView1.SelectedResource.Entity.GetAttributeValue<string>("name");
            Clipboard.SetText(name);

            MessageBox.Show(this,
                            string.Format("Web resource name ({0}) copied to clipboard", name),
                            Resources.MessageBox_InformationTitle,
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
        }

        private void TsmiGetLatestVersionClick(object sender, EventArgs e)
        {
            var selectedWr = webresourceTreeView1.SelectedResource;
            if (selectedWr.Entity == null || selectedWr.Entity.Id == Guid.Empty)
            {
                MessageBox.Show(ParentForm,
                    "This web resource has not been synchronized to CRM server yet. You cannot get latest version",
                    Resources.MessageBox_ErrorTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Retrieving latest version...",
                AsyncArgument = selectedWr,
                Work = (bw, evt) =>
                {
                    var wrm = new AppCode.WebResourceManager(Service);
                    var wr = wrm.RetrieveWebResource(((WebResource)evt.Argument).Entity.Id);

                    ((WebResource)evt.Argument).Entity = wr;
                    ((WebResource)evt.Argument).InitialBase64 = wr.GetAttributeValue<string>("content");

                    evt.Result = evt.Argument;
                },
                PostWorkCallBack = evt =>
                {
                    tsbDoFolding.Checked = false;
                    ((WebResource)evt.Result).ReinitStatus();
                    DisplayWebResourceControl((WebResource)evt.Result, true);
                }
            });
        }

        private void TsmiPropertieClick(object sender, EventArgs e)
        {
            if (TreeViewHelper.CheckOnlyThisNode(webresourceTreeView1))
                return;

            webresourceTreeView1.SelectedNode.Tag = ((WebResource)webresourceTreeView1.SelectedNode.Tag).ShowProperties(Service, this);
        }

        private void TsmiRenameWebResourceClick(object sender, EventArgs e)
        {
            if (TreeViewHelper.CheckOnlyThisNode(webresourceTreeView1))
                return;

            var webResource = webresourceTreeView1.GetCheckedResources().FirstOrDefault();
            var name1 = webResource?.Entity.GetAttributeValue<string>("name");

            var renameWebResource = new RenameWebResourceDialog(name1);
            renameWebResource.StartPosition = FormStartPosition.CenterParent;

            if (renameWebResource.ShowDialog() == DialogResult.OK)
            {
                var name2 = renameWebResource.WebResourceName;

                if (name1 != name2)
                {
                    WorkAsync(new WorkAsyncInfo
                    {
                        Message = $"Trying to rename '{name1}' to '{name2}'",
                        Work = (bw, evt) =>
                        {
                            bw.ReportProgress(0, "Searching for a webresource with the same name...");

                            // Check if resource with the same name already exists
                            var query = new QueryExpression("webresource");
                            query.Criteria.AddCondition("name", ConditionOperator.Equal, name2);

                            var result = Service.RetrieveMultiple(query).Entities.Count;

                            if (result == 0)
                            {
                                bw.ReportProgress(0, "Identifying solutions that contain this web resource...");

                                // Find if the web resource is attached to solutions
                                var solutions = Service.RetrieveMultiple(new QueryExpression("solution")
                                {
                                    ColumnSet = new ColumnSet(true),
                                    Criteria = new FilterExpression
                                    {
                                        Conditions =
                                        {
                                            new ConditionExpression("uniquename", ConditionOperator.NotEqual, "Active"),
                                            new ConditionExpression("uniquename", ConditionOperator.NotEqual, "Default")
                                        }
                                    },
                                    LinkEntities =
                                    {
                                        new LinkEntity
                                        {
                                            LinkFromEntityName = "solution",
                                            LinkFromAttributeName = "solutionid",
                                            LinkToAttributeName = "solutionid",
                                            LinkToEntityName = "solutioncomponent",
                                            LinkCriteria = new FilterExpression
                                            {
                                                Conditions =
                                                {
                                                    new ConditionExpression("objectid", ConditionOperator.Equal,
                                                        webResource.Entity.Id)
                                                }
                                            }
                                        }
                                    }
                                }).Entities;

                                // It's safe to update web resource. Direct rename is not possible,
                                // but deletion with different name, but same ID will have same result
                                bw.ReportProgress(0, "Deleting web resource...");
                                Service.Delete(webResource.Entity.LogicalName, webResource.Entity.Id);
                                bw.ReportProgress(0, "Creating web resource...");
                                webResource.Entity.Attributes["name"] = name2;
                                Service.Create(webResource.Entity);

                                // Add new web resource to solutions as before
                                foreach (var solution in solutions)
                                {
                                    bw.ReportProgress(0, $"Adding back the web resource to solution '{solution.GetAttributeValue<string>("friendlyname")}'...");

                                    var request = new AddSolutionComponentRequest
                                    {
                                        AddRequiredComponents = false,
                                        ComponentId = webResource.Entity.Id,
                                        ComponentType = SolutionComponentType.WebResource,
                                        SolutionUniqueName = solution.GetAttributeValue<string>("uniquename")
                                    };

                                    Service.Execute(request);
                                }
                            }
                            else
                            {
                                MessageBox.Show("Resource with the same name already exist, rename impossible!",
                                    Resources.MessageBox_ErrorTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                            }
                        },
                        PostWorkCallBack = evt =>
                        {
                            if (evt.Error != null)
                            {
                                MessageBox.Show($"Web resource renaming failed for the following reason: {evt.Error.Message}",
                                      Resources.MessageBox_ErrorTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                            }

                            webresourceTreeView1.DisplayWebResources(Options.Instance.ExpandAllOnLoadingResources);
                            webresourceTreeView1.Enabled = true;
                        },
                        ProgressChanged = evt =>
                        {
                            SetWorkingMessage(evt.UserState.ToString());
                        }
                    });
                }
            }
        }

        private void UpdateFromDiskToolStripMenuItemClick(object sender, EventArgs e)
        {
            if (DialogResult.OK ==
                MessageBox.Show(this,
                                "You will now have to select a directory. Each web resources in the selected folder with a corresponding file in the directory selected (same name) will be updated with the local file content",
                                Resources.MessageBox_InformationTitle, MessageBoxButtons.OKCancel, MessageBoxIcon.Information))
            {
                // TODO Revoir pour utiliser un gestionnaire de fichier pour passer le contenu au treeview
                string message = TreeViewHelper.UpdateNodesContentWithLocalFiles(webresourceTreeView1.SelectedNode.Nodes);

                if (message.Length > 0)
                {
                    MessageBox.Show(this, message, Resources.MessageBox_InformationTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        #endregion TREEVIEW - Manage content

        #region WEBRESOURCE CONTENT - Actions

        private void CompareFiles(string crmFile, string diskFile)
        {
            var startInfo = new ProcessStartInfo(Options.Instance.CompareToolPath)
            {
                Arguments = string.Format("{0} \"{1}\" \"{2}\"", Options.Instance.CompareToolArgs, crmFile, diskFile)
            };
            Process.Start(startInfo);
        }

        private void FileMenuReplaceClick(object sender, EventArgs e)
        {
            if (((TabControl)(Parent).Parent).SelectedTab != Parent)
            {
                ((ToolStripDropDownItem)((ToolStrip)(((TabControl)(Parent).Parent).SelectedTab.Controls.Find("toolStripScriptContent", true)[0])).Items[0]).DropDownItems[1].PerformClick();
                return;
            }

            try
            {
                var ctrl = ((IWebResourceControl)tabOpenedResources.SelectedTab.Controls[0]);

                using (var ofd = new OpenFileDialog())
                {
                    #region OpenFileDialog properties

                    OpenFileDialogSettings(ctrl, ofd);

                    #endregion OpenFileDialog properties

                    if (ofd.ShowDialog() == DialogResult.OK)
                    {
                        ctrl.ReplaceWithNewFile(ofd.FileName);
                    }
                }
            }
            catch (AccessViolationException error)
            {
                MessageBox.Show(error.ToString());
            }
        }

        private void saveAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (((TabControl)(Parent).Parent).SelectedTab != Parent)
            {
                ((ToolStripDropDownItem)((ToolStrip)((TabControl)Parent.Parent).SelectedTab.Controls.Find("toolStripScriptContent", true)[0]).Items[0]).DropDownItems[0].PerformClick();
                return;
            }

            foreach (TabPage tab in tabOpenedResources.TabPages)
            {
                InnerSave((IWebResourceControl)tab.Controls[0]);
            }
        }

        private void FileMenuSaveClick(object sender, EventArgs e)
        {
            if (((TabControl)(Parent).Parent).SelectedTab != Parent)
            {
                ((ToolStripDropDownItem)((ToolStrip)((TabControl)Parent.Parent).SelectedTab.Controls.Find("toolStripScriptContent", true)[0]).Items[0]).DropDownItems[0].PerformClick();
                return;
            }

            var control = (IWebResourceControl)tabOpenedResources.SelectedTab.Controls[0];
            InnerSave(control);
        }

        private void InnerSave(IWebResourceControl control)
        {
            TabPage tab = (TabPage)((Control) control).Parent;
            string content = control.GetBase64WebResourceContent();

            var webResource = (WebResource)webresourceTreeView1.SelectedNode.Tag;
            webResource.Entity["content"] = content;
            webResource.Save();
            //webResource.State = WebresourceState.Saved;

            fileMenuSave.Enabled = false;
            fileMenuUpdateAndPublish.Enabled = true;

            if (tab == tabOpenedResources.SelectedTab)
            {
                if (lblWebresourceName.Text.Contains(" (not saved)"))
                {
                    lblWebresourceName.Text = lblWebresourceName.Text.Replace(" (not saved)", " (not published)");
                    lblWebresourceName.ForeColor = Color.Blue;
                }
            }

            tab.Text = tab.Text.Replace(" *", " !");

            // Save on disk in options tells so and a filepath is provided
            if (Options.Instance.SaveOnDisk && !string.IsNullOrEmpty(webResource.FilePath))
            {
                // Ensure the file is not readonly before saving it
                FileInfo info = new FileInfo(webResource.FilePath);
                var initialState = info.IsReadOnly;
                info.IsReadOnly = false;

                using (StreamWriter writer = new StreamWriter(webResource.FilePath, false))
                {
                    writer.Write(webResource.GetPlainText());
                }

                info.IsReadOnly = initialState;
            }
        }

        private void FileMenuUpdateAndPublish(TreeNode node)
        {
            UpdateWebResources(true, new List<WebResource> { (WebResource)node.Tag });
        }

        private void FileMenuUpdateAndPublishClick(object sender, EventArgs e)
        {
            if (((TabControl)(Parent).Parent).SelectedTab != Parent)
            {
                ((ToolStripDropDownItem)((ToolStrip)(((TabControl)(Parent).Parent).SelectedTab.Controls.Find("toolStripScriptContent", true)[0])).Items[0]).DropDownItems[2].PerformClick();
                return;
            }

            if (webresourceTreeView1.SelectedNode != null)
            {
                ExecuteMethod(FileMenuUpdateAndPublish, webresourceTreeView1.SelectedNode);
            }
        }

        private void FindToolStripMenuItemClick(object sender, EventArgs e)
        {
            if (((TabControl)(Parent).Parent).SelectedTab != Parent)
            {
                ((ToolStripDropDownItem)((ToolStrip)(((TabControl)(Parent).Parent).SelectedTab.Controls.Find("toolStripScriptContent", true)[0])).Items[2]).DropDownItems[0].PerformClick();
                return;
            }

            if (tabOpenedResources.SelectedTab == null || tabOpenedResources.SelectedTab.Controls.Count == 0)
            {
                return;
            }

            var control = ((IWebResourceControl)tabOpenedResources.SelectedTab.Controls[0]);
            if (!(control is CodeControl)) return;

            ((CodeControl)control).Find(false, this);
        }

        private void OpenCompareFileDialogSettings(IWebResourceControl ctrl, OpenFileDialog ofd)
        {
            switch (ctrl.GetWebResourceType())
            {
                case Enumerations.WebResourceType.Script:
                    {
                        ofd.Title = string.Format(OpenfileTitleMask, "script file");
                        ofd.Filter = "Javascript file (*.js)|*.js";
                    }
                    break;

                case Enumerations.WebResourceType.WebPage:
                    {
                        ofd.Title = string.Format(OpenfileTitleMask, "web page");
                        ofd.Filter = "Web page (*.html,*.htm)|*.html;*.htm";
                    }
                    break;

                case Enumerations.WebResourceType.Css:
                    {
                        ofd.Title = string.Format(OpenfileTitleMask, "css file");
                        ofd.Filter = "Stylesheet (*.css)|*.css";
                    }
                    break;

                case Enumerations.WebResourceType.Xsl:
                    {
                        ofd.Title = string.Format(OpenfileTitleMask, "xslt file");
                        ofd.Filter = "Transformation file (*.xslt)|*.xslt";
                    }
                    break;

                case Enumerations.WebResourceType.Data:
                    {
                        ofd.Title = string.Format(OpenfileTitleMask, "xml file");
                        ofd.Filter = "Xml file (*.xml)|*.xml";
                    }
                    break;
            }
        }

        private void OpenCompareSettings(bool isConfigured = true)
        {
            using (var compareDialog = new CompareSettingsDialog(isConfigured))
            {
                compareDialog.ShowDialog();
            }
        }

        private void OpenFileDialogSettings(IWebResourceControl ctrl, OpenFileDialog ofd)
        {
            switch (ctrl.GetWebResourceType())
            {
                case Enumerations.WebResourceType.Gif:
                    {
                        ofd.Title = string.Format(OpenfileTitleMask, "image");
                        ofd.Filter = "Gif file (*.gif)|*.gif";
                    }
                    break;

                case Enumerations.WebResourceType.Jpg:
                    {
                        ofd.Title = string.Format(OpenfileTitleMask, "image");
                        ofd.Filter = "JPG file (*.jpg)|*.jpg";
                    }
                    break;

                case Enumerations.WebResourceType.Png:
                    {
                        ofd.Title = string.Format(OpenfileTitleMask, "image");
                        ofd.Filter = "PNG file (*.png)|*.png";
                    }
                    break;

                case Enumerations.WebResourceType.Ico:
                    {
                        ofd.Title = string.Format(OpenfileTitleMask, "icon");
                        ofd.Filter = "ICO file (*.ico)|*.ico";
                    }
                    break;

                case Enumerations.WebResourceType.Script:
                    {
                        ofd.Title = string.Format(OpenfileTitleMask, "script file");
                        ofd.Filter = "Javascript file (*.js)|*.js";
                    }
                    break;

                case Enumerations.WebResourceType.WebPage:
                    {
                        ofd.Title = string.Format(OpenfileTitleMask, "web page");
                        ofd.Filter = "Web page (*.html,*.htm)|*.html;*.htm";
                    }
                    break;

                case Enumerations.WebResourceType.Css:
                    {
                        ofd.Title = string.Format(OpenfileTitleMask, "css file");
                        ofd.Filter = "Stylesheet (*.css)|*.css";
                    }
                    break;

                case Enumerations.WebResourceType.Xsl:
                    {
                        ofd.Title = string.Format(OpenfileTitleMask, "xslt file");
                        ofd.Filter = "Transformation file (*.xslt)|*.xslt";
                    }
                    break;

                case Enumerations.WebResourceType.Silverlight:
                    {
                        ofd.Title = string.Format(OpenfileTitleMask, "Silverlight application");
                        ofd.Filter = "Silverlight application file (*.xap)|*.xap";
                    }
                    break;
            }
        }

        private void RemoveOldFiles()
        {
            var directory = new DirectoryInfo(string.Format(@"{0}\CompareTemp", Environment.CurrentDirectory));
            if (!Directory.Exists(directory.FullName))
                return;

            Directory.Delete(directory.FullName, true);
        }

        private void ReplaceToolStripMenuItemClick(object sender, EventArgs e)
        {
            if (((TabControl)(Parent).Parent).SelectedTab != Parent)
            {
                ((ToolStripDropDownItem)((ToolStrip)(((TabControl)(Parent).Parent).SelectedTab.Controls.Find("toolStripScriptContent", true)[0])).Items[2]).DropDownItems[1].PerformClick();
                return;
            }

            if (tabOpenedResources.SelectedTab.Controls.Count == 0)
                return;

            var control = ((IWebResourceControl)tabOpenedResources.SelectedTab.Controls[0]);
            if (!(control is CodeControl)) return;

            ((CodeControl)control).Find(true, this);
        }

        private string SaveContentFileToDisk(string fileName, string content)
        {
            var path = string.Format(@"{0}\CompareTemp", Environment.CurrentDirectory);
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);

            var filePath = string.Format(@"{0}\{1}", path, fileName);
            File.WriteAllBytes(filePath, Convert.FromBase64String(content));
            return filePath;
        }

        private void tsbBeautify_Click(object sender, EventArgs e)
        {
            ((CodeControl)tabOpenedResources.SelectedTab.Controls[0]).Beautify();
        }

        private void tsbComment_Click(object sender, EventArgs e)
        {
            ((CodeControl)tabOpenedResources.SelectedTab.Controls[0]).CommentSelectedLines();
        }

        private void TsbCompareClick(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Properties.Settings.Default.CompareToolPath))
            {
                OpenCompareSettings(false);
                return;
            }

            try
            {
                var ctrl = ((IWebResourceControl)tabOpenedResources.SelectedTab.Controls[0]);
                var content = ((IWebResourceControl)tabOpenedResources.SelectedTab.Controls[0]).GetBase64WebResourceContent();

                RemoveOldFiles();
                var crmFileToComapre = SaveContentFileToDisk(webresourceTreeView1.SelectedNode.Text, content);

                using (var ofd = new OpenFileDialog())
                {
                    OpenCompareFileDialogSettings(ctrl, ofd);

                    if (ofd.ShowDialog() == DialogResult.OK)
                    {
                        var diskfileToCompare = ofd.FileName;
                        CompareFiles(crmFileToComapre, diskfileToCompare);
                    }
                }
            }
            catch (Exception error)
            {
                MessageBox.Show(string.Format("Error while performing the file compare.{0}Please go to the compare settings and validate that you configured the correct compare tool.{0}{0}Error: {1}", Environment.NewLine, error.Message), Resources.MessageBox_ErrorTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void tsbDoFolding_Click(object sender, EventArgs e)
        {
            tsbDoFolding.Checked = !tsbDoFolding.Checked;
            ((CodeControl)tabOpenedResources.SelectedTab.Controls[0]).EnableFolding(tsbDoFolding.Checked);
        }

        private void TsbMinifyJsClick(object sender, EventArgs e)
        {
            if (DialogResult.Yes ==
                MessageBox.Show(this,
                                "Are you sure you want to compress this script? After saving the compressed script, you won't be able to retrieve original content",
                                Resources.MessageBox_QuestionTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question))
                ((CodeControl)tabOpenedResources.SelectedTab.Controls[0]).MinifyJs();
        }

        private void tsbnUncomment_Click(object sender, EventArgs e)
        {
            ((CodeControl)tabOpenedResources.SelectedTab.Controls[0]).UncommentSelectedLines();
        }

        private void TsbPreviewHtmlClick(object sender, EventArgs e)
        {
            string content = ((IWebResourceControl)tabOpenedResources.SelectedTab.Controls[0]).GetBase64WebResourceContent();

            var wpDialog = new WebPreviewDialog(content);
            wpDialog.ShowDialog();
        }

        private void TsmCompareSettingsClick(object sender, EventArgs e)
        {
            OpenCompareSettings();
        }

        private void refreshFromDiskToolStripEditorMenuItem_Click(object sender, EventArgs e)
        {
            TsmiRefreshFromDiskClick(null, null);
        }

        #endregion WEBRESOURCE CONTENT - Actions

        private void MainFormWebResourceUpdated(object sender, WebResourceUpdatedEventArgs e)
        {
            fileMenuSave.Enabled = e.IsDirty;
            fileMenuUpdateAndPublish.Enabled = !e.IsDirty;
            if (e.IsDirty)
            {
                ((WebResource) tabOpenedResources.SelectedTab.Tag).UpdatedBase64Content = e.Base64Content;
                //((WebResource)tabOpenedResources.SelectedTab.Tag).State = WebresourceState.Draft;

                if (!lblWebresourceName.Text.Contains(" (not saved)"))
                {
                    lblWebresourceName.ForeColor = Color.Red;
                    lblWebresourceName.Text = lblWebresourceName.Text.Split(' ')[0] + " (not saved)";
                    tabOpenedResources.SelectedTab.Text = lblWebresourceName.Text.Split(' ')[0] + " *";
                }
            }
            else
            {
                lblWebresourceName.ForeColor = Color.Black;
                lblWebresourceName.Text = lblWebresourceName.Text.Split(' ')[0];
                tabOpenedResources.SelectedTab.Text = lblWebresourceName.Text.Split(' ')[0];
            }
        }

        private void SetWorkingState(bool working)
        {
            tsbNewRoot.Enabled = !working;
            tsddCrmMenu.Enabled = !working;
            tsddFileMenu.Enabled = !working;
            webresourceTreeView1.Enabled = !working;
            tsbClear.Enabled = !working;
            toolStripScriptContent.Enabled = !working;
            findUnusedWebResourcesToolStripMenuItem.Enabled = !working;

            fileMenuSave.Enabled = false;
            var selectedNode = webresourceTreeView1.SelectedNode;
            if (selectedNode != null)
            {
                fileMenuReplace.Enabled = selectedNode.Tag != null && tabOpenedResources.SelectedTab != null;
                fileMenuUpdateAndPublish.Enabled = selectedNode.Tag != null && tabOpenedResources.SelectedTab != null;
                getLatestVersionToolStripMenuItem.Enabled = selectedNode.Tag != null && tabOpenedResources.SelectedTab != null;
            }

            Cursor = working ? Cursors.WaitCursor : Cursors.Default;
        }

        #endregion Methods

        #region ThisControl handler

        private void TsbCloseThisTabClick(object sender, EventArgs e)
        {
            CloseTool();
        }

        #endregion ThisControl handler

        private void tsbSettings_Click(object sender, EventArgs e)
        {
            var dialog = new OptionsDialog();
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                Options.Instance.Save();
            }
        }

        private void TsmiCollapseIncludingChildrensClick(object sender, EventArgs e)
        {
            webresourceTreeView1.SelectedNode.Collapse(false);
        }

        private void TsmiExpandincludingChildrensClick(object sender, EventArgs e)
        {
            webresourceTreeView1.SelectedNode.ExpandAll();
        }

        private void TsmiFindUnusedWebResourcesClick(object sender, EventArgs e)
        {
            var allresources = webresourceTreeView1.GetAllResources();
            WorkAsync(new WorkAsyncInfo("Starting analysis...", (bw, evt) =>
            {
                var resources = (List<WebResource>)evt.Argument;

                var unusedWebResources = new List<Entity>();
                int i = 1;
                foreach (var resource in resources)
                {
                    var wr = resource.Entity;

                    bw.ReportProgress((i * 100) / resources.Count, "Analyzing web resource " + wr["name"] + "...");

                    wrManager = new AppCode.WebResourceManager(Service);
                    if (!wrManager.HasDependencies(wr.Id))
                    {
                        unusedWebResources.Add(wr);
                    }
                    i++;
                }

                evt.Result = unusedWebResources;
            })
            {
                AsyncArgument = allresources,
                ProgressChanged = evt => SetWorkingMessage(string.Format("{0}% - {1}", evt.ProgressPercentage, evt.UserState)),
                PostWorkCallBack = evt =>
                {
                    var dialog = new UnusedWebResourcesListDialog((List<Entity>)evt.Result, Service);
                    dialog.ShowInTaskbar = true;
                    dialog.StartPosition = FormStartPosition.CenterParent;
                    dialog.ShowDialog(this);
                }
            });
        }

        private void TsmiOpenWebResourceRecordInCrmApplicationClick(object sender, EventArgs e)
        {
            var wr = ((WebResource)webresourceTreeView1.SelectedNode.Tag).Entity;

            if (wr.Id != Guid.Empty)
            {
                var url = string.Format("{0}/main.aspx?id={1}&etc=9333&pagetype=webresourceedit", ConnectionDetail.WebApplicationUrl, wr.Id);
                Process.Start(url);
            }
            else
            {
                MessageBox.Show(this, "This web resource does not exist on the CRM organization yet", "Warning",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void webresourceTreeView1_WebResourceContextMenuRequested(object sender, WebResourceContextMenuRequestedEventArgs e)
        {
            switch (webresourceTreeView1.SelectedNode.ImageIndex)
            {
                // Top-level: publisher prefix
                case 0:
                case 12:
                    {
                        addNewFolderToolStripMenuItem.Enabled = true;
                        addNewWebResourceToolStripMenuItem.Enabled = true;
                        addNewEmptyWebResourceToolStripMenuItem.Enabled = true;
                        deleteToolStripMenuItem.Enabled = webresourceTreeView1.SelectedNode.Nodes.Count == 0;
                        saveToCRMServerToolStripMenuItem.Enabled = false;
                        saveAndPublishToCRMServerToolStripMenuItem.Enabled = false;
                        savePublishAndAddToSolutionToolStripMenuItem.Enabled = false;
                        propertiesToolStripMenuItem.Enabled = false;
                        updateFromDiskToolStripMenuItem.Enabled = true;
                        copyWebResourceNameToClipboardToolStripMenuItem.Enabled = false;
                        getLatestVersionToolStripMenuItem.Enabled = false;
                        renameWebResourceToolStripMenuItem.Enabled = false;

                        expandincludingChildrensToolStripMenuItem.Visible = !webresourceTreeView1.SelectedNode.IsExpanded;
                        collapseIncludingChildrensToolStripMenuItem.Visible = webresourceTreeView1.SelectedNode.IsExpanded;
                        toolStripSeparatorExpandCollapse.Visible = true;
                    }
                    break;

                // First-level: virtual folder
                case 1:
                case 13:
                    {
                        addNewFolderToolStripMenuItem.Enabled = true;
                        addNewWebResourceToolStripMenuItem.Enabled = true;
                        addNewEmptyWebResourceToolStripMenuItem.Enabled = true;
                        deleteToolStripMenuItem.Enabled = webresourceTreeView1.SelectedNode.Nodes.Count == 0;
                        saveToCRMServerToolStripMenuItem.Enabled = false;
                        saveAndPublishToCRMServerToolStripMenuItem.Enabled = false;
                        savePublishAndAddToSolutionToolStripMenuItem.Enabled = false;
                        propertiesToolStripMenuItem.Enabled = false;
                        updateFromDiskToolStripMenuItem.Enabled = true;
                        copyWebResourceNameToClipboardToolStripMenuItem.Enabled = false;
                        getLatestVersionToolStripMenuItem.Enabled = false;
                        renameWebResourceToolStripMenuItem.Enabled = false;

                        expandincludingChildrensToolStripMenuItem.Visible = !webresourceTreeView1.SelectedNode.IsExpanded;
                        collapseIncludingChildrensToolStripMenuItem.Visible = webresourceTreeView1.SelectedNode.IsExpanded;
                        toolStripSeparatorExpandCollapse.Visible = true;
                    }
                    break;

                // Default-level: resource name
                default:
                    {
                        addNewFolderToolStripMenuItem.Enabled = false;
                        addNewWebResourceToolStripMenuItem.Enabled = false;
                        addNewEmptyWebResourceToolStripMenuItem.Enabled = false;
                        deleteToolStripMenuItem.Enabled = true;
                        saveToCRMServerToolStripMenuItem.Enabled = true;
                        saveAndPublishToCRMServerToolStripMenuItem.Enabled = true;
                        savePublishAndAddToSolutionToolStripMenuItem.Enabled = true;
                        propertiesToolStripMenuItem.Enabled = true;
                        updateFromDiskToolStripMenuItem.Enabled = false;
                        copyWebResourceNameToClipboardToolStripMenuItem.Enabled = true;
                        getLatestVersionToolStripMenuItem.Enabled = true;
                        renameWebResourceToolStripMenuItem.Enabled = true;

                        expandincludingChildrensToolStripMenuItem.Visible = false;
                        collapseIncludingChildrensToolStripMenuItem.Visible = false;
                        toolStripSeparatorExpandCollapse.Visible = false;
                    }
                    break;
            }

            if (webresourceTreeView1.SelectedNode != null)
            {
                cmsWebResourceTreeView.Show(webresourceTreeView1, e.Location);
            }
        }

        private void webresourceTreeView1_WebResourceSelected(object sender, WebResourceSelectedEventArgs e)
        {
            tsbDoFolding.Checked = false;
            var resource = e.WebResource;
            DisplayWebResourceControl(resource);
        }

        private void DisplayWebResourceControl(WebResource resource, bool replaceContent = false)
        {
            if (resource != null)
            {
                toolStripScriptContent.Visible = true;
                lblWebresourceName.Visible = true;

                // Displays script content
                Entity script = resource.Entity;
                string resourceName = script.GetAttributeValue<string>("name");
                UserControl ctrl = null;

                refreshFromDiskToolStripEditorMenuItem.Visible = !string.IsNullOrEmpty(resource.FilePath);
                refreshFromDiskToolStripEditorMenuItem.Enabled = !string.IsNullOrEmpty(resource.FilePath);

                switch (((OptionSetValue)script["webresourcetype"]).Value)
                {
                    case 1:
                        ctrl = new CodeControl(script.GetAttributeValue<string>("content"),
                                                Enumerations.WebResourceType.WebPage);
                        ((CodeControl)ctrl).WebResourceUpdated +=
                            MainFormWebResourceUpdated;
                        toolStripSeparatorMinifyJS.Visible = true;
                        tsbMinifyJS.Visible = false;
                        tsbDoFolding.Visible = true;
                        tsbBeautify.Visible = false;
                        tsbPreviewHtml.Visible = true;
                        tsSeparatorEdit.Visible = true;
                        tsddbEdit.Visible = true;
                        tsddbCompare.Visible = true;
                        tsbComment.Visible = true;
                        tsbnUncomment.Visible = true;
                        break;

                    case 2:
                        ctrl = new CodeControl(script.GetAttributeValue<string>("content"),
                                                Enumerations.WebResourceType.Css);
                        ((CodeControl)ctrl).WebResourceUpdated += MainFormWebResourceUpdated;
                        tsbMinifyJS.Visible = false;
                        tsbDoFolding.Visible = true;
                        tsbBeautify.Visible = false;
                        tsbPreviewHtml.Visible = false;
                        tsSeparatorEdit.Visible = true;
                        tsddbEdit.Visible = true;
                        tsddbCompare.Visible = true;
                        tsbComment.Visible = true;
                        tsbnUncomment.Visible = true;
                        break;

                    case 3:
                        ctrl = new CodeControl(script.GetAttributeValue<string>("content"),
                                                Enumerations.WebResourceType.Script);
                        ((CodeControl)ctrl).WebResourceUpdated +=
                            MainFormWebResourceUpdated;
                        toolStripSeparatorMinifyJS.Visible = true;
                        tsbDoFolding.Visible = true;
                        tsbMinifyJS.Visible = true;
                        tsbBeautify.Visible = true;
                        tsbPreviewHtml.Visible = false;
                        tsSeparatorEdit.Visible = true;
                        tsddbEdit.Visible = true;
                        tsddbCompare.Visible = true;
                        tsbComment.Visible = true;
                        tsbnUncomment.Visible = true;
                        break;

                    case 4:
                        ctrl = new CodeControl(script.GetAttributeValue<string>("content"),
                                                Enumerations.WebResourceType.Data);
                        ((CodeControl)ctrl).WebResourceUpdated +=
                            MainFormWebResourceUpdated;
                        tsbMinifyJS.Visible = false;
                        tsbDoFolding.Visible = true;
                        tsbBeautify.Visible = false;
                        tsbPreviewHtml.Visible = false;
                        tsSeparatorEdit.Visible = true;
                        tsddbEdit.Visible = true;
                        tsddbCompare.Visible = true;
                        tsbComment.Visible = true;
                        tsbnUncomment.Visible = true;
                        break;

                    case 5:
                        ctrl = new ImageControl(script.GetAttributeValue<string>("content"),
                                                Enumerations.WebResourceType.Png);
                        ((ImageControl)ctrl).WebResourceUpdated +=
                            MainFormWebResourceUpdated;
                        tsbMinifyJS.Visible = false;
                        tsbDoFolding.Visible = false;
                        tsbBeautify.Visible = false;
                        tsbPreviewHtml.Visible = false;
                        tsSeparatorEdit.Visible = false;
                        tsddbEdit.Visible = false;
                        tsddbCompare.Visible = false;
                        tsbComment.Visible = false;
                        tsbnUncomment.Visible = false;
                        break;

                    case 6:
                        ctrl = new ImageControl(script.GetAttributeValue<string>("content"),
                                                Enumerations.WebResourceType.Jpg);
                        ((ImageControl)ctrl).WebResourceUpdated +=
                            MainFormWebResourceUpdated;
                        tsbMinifyJS.Visible = false;
                        tsbDoFolding.Visible = false;
                        tsbBeautify.Visible = false;
                        tsbPreviewHtml.Visible = false;
                        tsSeparatorEdit.Visible = false;
                        tsddbEdit.Visible = false;
                        tsddbCompare.Visible = false;
                        tsbComment.Visible = false;
                        tsbnUncomment.Visible = false;
                        break;

                    case 7:
                        ctrl = new ImageControl(script.GetAttributeValue<string>("content"),
                                                Enumerations.WebResourceType.Gif);
                        ((ImageControl)ctrl).WebResourceUpdated +=
                            MainFormWebResourceUpdated;
                        tsbMinifyJS.Visible = false;
                        tsbDoFolding.Visible = false;
                        tsbBeautify.Visible = false;
                        tsbPreviewHtml.Visible = false;
                        tsSeparatorEdit.Visible = false;
                        tsddbEdit.Visible = false;
                        tsddbCompare.Visible = false;
                        tsbComment.Visible = false;
                        tsbnUncomment.Visible = false;
                        break;

                    case 8:
                        ctrl = new UserControl();
                        tsSeparatorEdit.Visible = false;
                        tsddbEdit.Visible = false;
                        tsbDoFolding.Visible = false;
                        tsbPreviewHtml.Visible = false;
                        tsddbCompare.Visible = false;
                        tsbComment.Visible = false;
                        tsbnUncomment.Visible = false;
                        break;

                    case 9:
                        ctrl = new CodeControl(script.GetAttributeValue<string>("content"),
                                                Enumerations.WebResourceType.Xsl);
                        ((CodeControl)ctrl).WebResourceUpdated +=
                            MainFormWebResourceUpdated;
                        tsbMinifyJS.Visible = false;
                        tsbDoFolding.Visible = false;
                        tsbBeautify.Visible = true;
                        tsbPreviewHtml.Visible = false;
                        tsSeparatorEdit.Visible = true;
                        tsddbEdit.Visible = true;
                        tsddbCompare.Visible = true;
                        tsbComment.Visible = true;
                        tsbnUncomment.Visible = true;
                        break;

                    case 10:
                        ctrl = new IconControl(script.GetAttributeValue<string>("content"));
                        ((IconControl)ctrl).WebResourceUpdated +=
                            MainFormWebResourceUpdated;
                        tsbMinifyJS.Visible = false;
                        tsbDoFolding.Visible = false;
                        tsbBeautify.Visible = false;
                        tsbPreviewHtml.Visible = false;
                        tsSeparatorEdit.Visible = false;
                        tsddbEdit.Visible = false;
                        tsddbCompare.Visible = false;
                        tsbComment.Visible = false;
                        tsbnUncomment.Visible = false;
                        break;
                }

                if (ctrl != null)
                {
                    ctrl.Name = "webresourceContentControl";
                    ctrl.Dock = DockStyle.Fill;

                    if (tabOpenedResources.TabPages.ContainsKey(resourceName))
                    {
                        tabOpenedResources.SelectedIndex = tabOpenedResources.TabPages.IndexOfKey(resourceName);
                        switch (resource.State)
                        {
                            case WebresourceState.Saved:
                                {
                                    tabOpenedResources.SelectedTab.Text = string.Format("{0} !",
                                        resource.Entity.GetAttributeValue<string>("name"));
                                    tabOpenedResources.SelectedTab.ForeColor = Color.Blue;
                                    break;
                                }
                            case WebresourceState.Draft:
                                {
                                    tabOpenedResources.SelectedTab.Text = string.Format("{0} *",
                                        resource.Entity.GetAttributeValue<string>("name"));
                                    tabOpenedResources.SelectedTab.ForeColor = Color.Red;
                                    break;
                                }
                            default:
                                {
                                    tabOpenedResources.SelectedTab.Text =
                                        resource.Entity.GetAttributeValue<string>("name");
                                    tabOpenedResources.SelectedTab.ForeColor = Color.Black;
                                    break;
                                }
                        }

                        if (replaceContent)
                        {
                            tabOpenedResources.SelectedTab.Controls.Clear();
                            tabOpenedResources.SelectedTab.Controls.Add(ctrl);
                        }
                    }
                    else
                    {
                        var newTab = new TabPage
                        {
                            Text = resourceName,
                            Name = resourceName,
                            Tag = resource
                        };
                        newTab.MouseClick += (s, evt) =>
                        {
                            if (evt.Button == MouseButtons.Middle)
                            {
                                var codeCtrl = tabOpenedResources.SelectedTab.Controls[0] as CodeControl;

                                if (codeCtrl != null && codeCtrl.IsDirty)
                                {
                                    if (Options.Instance.AutoSaveWhenLeaving)
                                    {
                                        fileMenuSave.PerformClick();
                                    }
                                    else
                                    {
                                        if (MessageBox.Show(this, "The webresource has pending changes!\r\n\r\nWould you like to save the content before closing this tab?", "Warning", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                                        {
                                            fileMenuSave.PerformClick();
                                        }
                                    }
                                }

                                tabOpenedResources.TabPages.Remove(tabOpenedResources.SelectedTab);
                            }
                        };
                        newTab.Controls.Add(ctrl);
                        tabOpenedResources.TabPages.Add(newTab);
                        tabOpenedResources.SelectedTab = newTab;

                        fileMenuSave.Enabled = false;
                        fileMenuReplace.Enabled = true;
                        fileMenuUpdateAndPublish.Enabled = true;

                        lblWebresourceName.Text = resourceName;
                        tabOpenedResources.SelectedTab.Text = resourceName;

                        if (resource.State == WebresourceState.Draft)
                        {
                            lblWebresourceName.ForeColor = Color.Red;
                            lblWebresourceName.Text = string.Format("{0} (not saved)", resourceName);
                            tabOpenedResources.SelectedTab.Text = lblWebresourceName.Text;
                        }
                        else if (resource.State == WebresourceState.Saved)
                        {
                            lblWebresourceName.ForeColor = Color.Blue;
                            lblWebresourceName.Text = string.Format("{0} (not published)", resourceName);
                            tabOpenedResources.SelectedTab.Text = lblWebresourceName.Text;
                        }
                    }

                    if (ctrl is CodeControl && ((CodeControl)ctrl).FoldingEnabled)
                    {
                        tsbDoFolding.Checked = true;
                    }
                }
                else
                {
                    fileMenuSave.Enabled = false;
                    fileMenuReplace.Enabled = false;
                    fileMenuUpdateAndPublish.Enabled = false;

                    toolStripSeparatorMinifyJS.Visible = false;
                    tsbMinifyJS.Visible = false;
                    tsbPreviewHtml.Visible = false;
                    tsbComment.Visible = false;
                    tsbnUncomment.Visible = false;

                    lblWebresourceName.Text = string.Empty;
                }
            }
            else
            {
                // Clear script content
                if (webresourceTreeView1.SelectedNode != null) webresourceTreeView1.SelectedNode.ContextMenuStrip = null;

                fileMenuSave.Enabled = false;
                fileMenuReplace.Enabled = false;
                fileMenuUpdateAndPublish.Enabled = false;
                toolStripScriptContent.Visible = false;
                lblWebresourceName.Visible = false;
            }
        }

        private void WebresourceTreeView1_WebResourceUpdateRequested(object sender, WebResourceUpdateRequestedEventArgs e)
        {
            UpdateWebResources(e.Action == WebResourceUpdateOption.UpdateAndPublish || e.Action == WebResourceUpdateOption.UpdateAndPublishAndAdd,
                e.WebResources,
                e.Action == WebResourceUpdateOption.UpdateAndPublishAndAdd);
        }

        private void goToLineToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (((TabControl)(Parent).Parent).SelectedTab != Parent)
            {
                ((ToolStripDropDownItem)((ToolStrip)(((TabControl)(Parent).Parent).SelectedTab.Controls.Find("toolStripScriptContent", true)[0])).Items[2]).DropDownItems[0].PerformClick();
                return;
            }

            if (tabOpenedResources.SelectedTab == null || tabOpenedResources.SelectedTab.Controls.Count == 0)
            {
                return;
            }

            var control = ((IWebResourceControl)tabOpenedResources.SelectedTab.Controls[0]);
            if (!(control is CodeControl))
            {
                return;
            }

           ((CodeControl)control).GoToLine();
        }

        #region Tabs management

        private void tabOpenedResources_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (tabOpenedResources.SelectedTab == null)
            {
                lblWebresourceName.Text = string.Empty;
                fileMenuSave.Enabled = false;
                fileMenuReplace.Enabled = false;
                fileMenuUpdateAndPublish.Enabled = false;
                getLatestVersionToolStripMenuItem.Enabled = false;
                toolStripScriptContent.Visible = false;
                return;
            }

            var wr = (WebResource) tabOpenedResources.SelectedTab.Tag;

            switch (wr.State)
            {
                case WebresourceState.Draft:
                {
                    tabOpenedResources.SelectedTab.ForeColor = Color.Red;
                    lblWebresourceName.ForeColor = Color.Red;
                    lblWebresourceName.Text = string.Format("{0} (not saved)",wr.Entity.GetAttributeValue<string>("name"));

                    fileMenuSave.Enabled = true;

                    break;
                }
                case WebresourceState.Saved:
                {
                    tabOpenedResources.SelectedTab.ForeColor = Color.Blue;
                    lblWebresourceName.ForeColor = Color.Blue;
                    lblWebresourceName.Text = string.Format("{0} (not published)", wr.Entity.GetAttributeValue<string>("name"));

                    fileMenuSave.Enabled = false;

                    break;
                }
                default:
                {
                    tabOpenedResources.SelectedTab.ForeColor = Color.Black;
                    lblWebresourceName.ForeColor = Color.Black;
                    lblWebresourceName.Text = wr.Entity.GetAttributeValue<string>("name");

                    fileMenuSave.Enabled = false;

                    break;
                }
            }

            webresourceTreeView1.SelectNode(((WebResource)tabOpenedResources.SelectedTab.Tag).Node);
        }

        private void closeThisTabToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CloseTabs(new List<TabPage> { rightClickedTabPage });
        }

        private void closeAllTabsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CloseTabs(tabOpenedResources.TabPages.Cast<TabPage>());
        }

        private void colseAllButThisTabToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var list = tabOpenedResources.TabPages.Cast<TabPage>().ToList();
            list.Remove(rightClickedTabPage);
            CloseTabs(list);
        }

        private void CloseTabs(IEnumerable<TabPage> tabs)
        {
            foreach (var tab in tabs)
            {
                tabOpenedResources.TabPages.Remove(tab);
            }
        }

        private TabPage rightClickedTabPage;
        private void tabOpenedResources_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                for (int i = 0; i < tabOpenedResources.TabCount; ++i)
                {
                    if (tabOpenedResources.GetTabRect(i).Contains(e.Location))
                    {
                        rightClickedTabPage = (TabPage)tabOpenedResources.Controls[i];
                    }
                }
                cmsTab.Show(tabOpenedResources, e.Location);
            }
        }

        #endregion

       
    }
}