﻿// --
// -- TestCop http://github.com/testcop
// -- License http://github.com/testcop/license
// -- Copyright 2013
// --

using System;
using System.Collections.Generic;
using System.Drawing;

using JetBrains.Application.DataContext;
using JetBrains.Application.UI.Controls;
using JetBrains.Application.UI.Controls.JetPopupMenu;
using JetBrains.Application.UI.DataContext;
using JetBrains.Application.UI.PopupLayout;
using JetBrains.DocumentManagers;
using JetBrains.DocumentModel;
using JetBrains.IDE;
using JetBrains.Lifetimes;
using JetBrains.Metadata.Reader.API;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Feature.Services.Navigation;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.Tree;
using JetBrains.ReSharper.Resources.Shell;
using JetBrains.UI.RichText;
using JetBrains.Util;

using TestCop.Plugin.Extensions;

namespace TestCop.Plugin.Helper
{
    using JetBrains.Util.Media;

    public static class JumpToTestMenuHelper
    {
        //------------------------------------------------------------------------------------------------------------------------
        public static void PromptToOpenOrCreateClassFiles(Action<JetPopupMenus, JetPopupMenu, JetPopupMenu.ShowWhen> menuDisplayer, Lifetime lifetime, IDataContext context, ISolution solution
    , IProject project, IClrTypeName clrTypeClassName, IList<TestCopProjectItem> targetProjects
    , List<IClrDeclaredElement> preferred, List<IClrDeclaredElement> fullList)
        {
            var autoExecuteIfSingleEnabledItem = JetPopupMenu.ShowWhen.AutoExecuteIfSingleEnabledItem;
            var menuItems = new List<SimpleMenuItem>();

            if (preferred.Count > 0)
            {
                AppendNavigateToMenuItems(lifetime, solution, preferred, menuItems);
            }
            else
            {
                AppendNavigateToMenuItems(lifetime, solution, fullList, menuItems);
            }

            MoveBestMatchesToTopWhenSwitchingFromTestToCode(menuItems, project, targetProjects, clrTypeClassName);

            if (clrTypeClassName != null)
            {
                if (DeriveRelatedFileNameAndAddCreateMenus(context, lifetime, project, targetProjects, menuItems, clrTypeClassName))
                {
                    autoExecuteIfSingleEnabledItem = JetPopupMenu.ShowWhen.NoItemsBannerIfNoItems;
                }
            }

            var menus = Shell.Instance.GetComponent<JetPopupMenus>();
            var menu = menus.Create();
            menu.Caption.Value = WindowlessControlAutomation.Create("Switch to:");
            menu.SetItems(menuItems.ToArray());

            PositionPopMenuCorrectly(context, lifetime, menu);

            menu.KeyboardAcceleration.SetValue(KeyboardAccelerationFlags.Mnemonics);
            menu.NoItemsBanner = WindowlessControlAutomation.Create("No destinations found.");

            menuDisplayer.Invoke(menus, menu, autoExecuteIfSingleEnabledItem);
        }
        //------------------------------------------------------------------------------------------------------------------------
        private static void AppendNavigateToMenuItems(Lifetime lifetime, ISolution solution, List<IClrDeclaredElement> clrDeclaredElements,
                                                      List<SimpleMenuItem> menuItems)
        {
            IEditorManager editorManager = solution.GetComponent<IEditorManager>();
            DocumentManager documentManager = solution.GetComponent<DocumentManager>();

            foreach (var declaredElement in clrDeclaredElements)
            {
                var simpleMenuItems = DescribeFilesAssociatedWithDeclaredElement(lifetime, documentManager,
                                                                                 declaredElement
                                                                                 ,
                                                                                 p => async () =>
                                                                                 await editorManager.OpenProjectFileAsync(p, new OpenFileOptions(FireAndForget: true)).ConfigureAwait(false)
                    );
                menuItems.AddRange(simpleMenuItems);
            }
        }
        //------------------------------------------------------------------------------------------------------------------------
        private static void PositionPopMenuCorrectly(IDataContext context, Lifetime lifetime, JetPopupMenu menu)
        {
            var windowContextSource = context.GetData<PopupWindowContextSource>(UIDataConstants.PopupWindowContextSource);

            if (windowContextSource != null)
            {
                menu.PopupWindowContextSource = windowContextSource;
            }
        }
        //------------------------------------------------------------------------------------------------------------------------
        private static IList<SimpleMenuItem> DescribeFilesAssociatedWithDeclaredElement(Lifetime lifetime, DocumentManager documentManager, IClrDeclaredElement declaredElement, Func<IProjectFile, Action> clickAction)
        {
            IList<SimpleMenuItem> menuItems = new List<SimpleMenuItem>();

            var projectFiles = GetProjectFiles(documentManager, declaredElement);

            foreach (var projectFile in projectFiles)
            {
                IProjectFile currentProjectFile = projectFile;
                var np = new ProjectFileNavigationPoint(currentProjectFile);

                SimpleMenuItemForProjectItem result = new SimpleMenuItemForProjectItem(np.GetPresentationText()
                    , np.GetPresentationImage()
                    , ResharperHelper.ProtectActionFromReEntry(lifetime, "TestingMenuNavigation", clickAction.Invoke(projectFile))
                    , projectFile, declaredElement
                )
                {
                    ShortcutText = np.GetSecondaryPresentationText(),
                    Style = MenuItemStyle.Enabled,
                    Tag = projectFile.Location.FullPath
                };


                menuItems.Add(result);
            }
            return menuItems;
        }

        public static void MoveBestMatchesToTopWhenSwitchingFromTestToCode(IList<SimpleMenuItem> currentMenus
            , IProject project
            , IList<TestCopProjectItem> associatedTargetProjects
            , IClrTypeName clrTypeClassName)
        {
            if (clrTypeClassName == null) return;

            foreach (string testSuffix in TestCopSettingsManager.Instance.Settings.TestClassSuffixes())
            {
                bool currentFileisTestFile = clrTypeClassName.ShortName.EndsWith(testSuffix);
                string targetFileName = clrTypeClassName.ShortName.Flip(currentFileisTestFile, testSuffix);

                foreach (var associatedTargetProject in associatedTargetProjects)
                {
                    var targetFilePathName =
                        FileSystemPath.Parse(associatedTargetProject.SubNamespaceFolder + "\\" + targetFileName);

                    for (int i = 0; i < currentMenus.Count; i++)
                    {
                        var menuItem = currentMenus[i];
                        if (menuItem.Tag == null) continue;

                        if (menuItem.Tag.ToString()
                            .StartsWith(targetFilePathName.FullPath, StringComparison.CurrentCultureIgnoreCase))
                        {
                            currentMenus.RemoveAt(i);
                            currentMenus.Insert(0, menuItem);
                        }
                    }
                }
            }
        }


        //------------------------------------------------------------------------------------------------------------------------
        public static bool DeriveRelatedFileNameAndAddCreateMenus(IDataContext context, Lifetime lifetime,
            IProject project, IList<TestCopProjectItem> associatedTargetProjects, IList<SimpleMenuItem> currentMenus,
            IClrTypeName clrTypeClassName)
        {
            bool addedCreateMenuItem = false;

            if (clrTypeClassName == null) return false;
            var baseFileName = ResharperHelper.GetBaseFileName(context, project.GetSolution());

            var settings = TestCopSettingsManager.Instance.Settings;
            bool currentFileisTestFile = baseFileName.EndsWith(settings.TestClassSuffixes());

            foreach (var testClassSuffix in settings.GetAppropriateTestClassSuffixes(baseFileName))
            {
                var targetFile = ResharperHelper.UsingFileNameGetClassName(baseFileName).RemoveTrailing(testClassSuffix);

                if (!currentFileisTestFile)
                {
                    targetFile += testClassSuffix;
                }

                foreach (var associatedTargetProjectItem in associatedTargetProjects)
                {
                    if (currentFileisTestFile == associatedTargetProjectItem.Project.IsTestProject())
                    {
                        ResharperHelper.AppendLineToOutputWindow(project.Locks,
                            string.Format("Internal Error: Attempted to create '{0}' within project '{1}'"
                                , targetFile, associatedTargetProjectItem.Project.Name));
                        continue;
                    }

                    string targetFileLocation = associatedTargetProjectItem.SubNamespaceFolder.FullPath + "\\" + targetFile;

                    if (!IsMenuItemPresentForFile(currentMenus, targetFileLocation))
                    {
                        currentMenus.AddRange(AddCreateFileMenuItem(lifetime, associatedTargetProjectItem, targetFile));
                        addedCreateMenuItem = true;
                    }
                }
            }

            return addedCreateMenuItem;
        }

        private static bool IsMenuItemPresentForFile(IList<SimpleMenuItem> currentMenus, string targetFileLocation)
        {
            foreach (var menuItem in currentMenus)
            {
                if (menuItem.Tag == null) continue;
                if (menuItem.Tag.ToString().StartsWith(targetFileLocation, StringComparison.CurrentCultureIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        //------------------------------------------------------------------------------------------------------------------------
        private static List<SimpleMenuItem> AddCreateFileMenuItem(Lifetime lifetime, TestCopProjectItem projectItem, string targetFile)
        {
            List<SimpleMenuItem> menuItems = new List<SimpleMenuItem>();

            SimpleMenuItem result = new SimpleMenuItem("Create associated file"
                , null
                , ResharperHelper.ProtectActionFromReEntry(lifetime, "TestingMenuNavigation"
                , () => ResharperHelper.CreateFileWithinProject(projectItem, targetFile)))
            {
                Style = MenuItemStyle.Enabled,
                Icon = UnnamedThemedIcons.Agent16x16.Id,
                Text = new RichText("Create ", TextStyle.FromForeColor(JetRgbaColor.FromArgb(Color.Green.A, Color.Green.R, Color.Green.G, Color.Green.B)))
                    .Append(targetFile, TextStyle.FromForeColor(TextStyle.DefaultForegroundColor)),
                ShortcutText = new RichText("(" + projectItem.Project.GetPresentableProjectPath()
                                                + projectItem.SubNamespaceFolder.FullPath.RemoveLeading(projectItem.Project.ProjectFileLocation.Directory.FullPath)
                                                + ")",
                    TextStyle.FromForeColor(JetRgbaColor.FromArgb(Color.LightGray.A, Color.LightGray.R, Color.LightGray.G, Color.LightGray.B)))
            };
            menuItems.Add(result);
            return menuItems;
        }
        //------------------------------------------------------------------------------------------------------------------------
        private static IList<IProjectFile> GetProjectFiles(DocumentManager documentManager, IDeclaredElement declaredElement)
        {
            IList<IProjectFile> results = new List<IProjectFile>();
            foreach (var declaration in declaredElement.GetDeclarations())
            {
                DocumentRange documentRange = declaration.GetNavigationRange();
                if (!documentRange.IsValid())
                    documentRange = TreeNodeExtensions.GetDocumentRange(declaration);

                if (documentRange.IsValid())
                {
                    IProjectFile projectFile = documentManager.TryGetProjectFile(documentRange.Document);
                    if (projectFile != null)
                    {
                        results.Add(projectFile);
                    }
                }
            }
            return results;
        }
        //------------------------------------------------------------------------------------------------------------------------
    }
}
