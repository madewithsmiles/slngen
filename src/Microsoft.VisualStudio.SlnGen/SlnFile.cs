﻿// Copyright (c) Microsoft Corporation.
//
// Licensed under the MIT license.

using Microsoft.Build.Evaluation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Microsoft.VisualStudio.SlnGen
{
    /// <summary>
    /// Represents a Visual Studio solution file.
    /// </summary>
    public sealed class SlnFile
    {
        /// <summary>
        /// The solution header.
        /// </summary>
        private const string Header = "Microsoft Visual Studio Solution File, Format Version {0}";

        /// <summary>
        /// The file format version.
        /// </summary>
        private readonly string _fileFormatVersion;

        /// <summary>
        /// Gets the projects.
        /// </summary>
        private readonly List<SlnProject> _projects = new List<SlnProject>();

        /// <summary>
        /// A list of absolute paths to include as Solution Items.
        /// </summary>
        private readonly List<string> _solutionItems = new List<string>();

        /// <summary>
        /// Initializes a new instance of the <see cref="SlnFile" /> class.
        /// </summary>
        /// <param name="fileFormatVersion">The file format version.</param>
        public SlnFile(string fileFormatVersion)
        {
            _fileFormatVersion = fileFormatVersion;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SlnFile" /> class.
        /// </summary>
        public SlnFile()
            : this("12.00")
        {
        }

        /// <summary>
        /// A <see cref="IReadOnlyCollection{String}" /> of Configuration values to use.
        /// </summary>
        public IReadOnlyCollection<string> Configurations { get; set; }

        /// <summary>
        /// A <see cref="IReadOnlyCollection{String}" /> of Platform values to use.
        /// </summary>
        public IReadOnlyCollection<string> Platforms { get; set; }

        /// <summary>
        /// Gets a list of solution items.
        /// </summary>
        public IReadOnlyCollection<string> SolutionItems => _solutionItems;

        /// <summary>
        /// Adds the specified projects.
        /// </summary>
        /// <param name="projects">An <see cref="IEnumerable{SlnProject}"/> containing projects to add to the solution.</param>
        public void AddProjects(IEnumerable<SlnProject> projects)
        {
            _projects.AddRange(projects);
        }

        public void AddProjects(IEnumerable<Project> projects, IReadOnlyDictionary<string, Guid> customProjectTypeGuids, string mainProjectFullPath)
        {
            _projects.AddRange(
                projects
                    .Distinct(new EqualityComparer<Project>((x, y) => string.Equals(x.FullPath, y.FullPath, StringComparison.OrdinalIgnoreCase), i => i.FullPath.GetHashCode()))
                    .Select(i => SlnProject.FromProject(i, customProjectTypeGuids, string.Equals(i.FullPath, mainProjectFullPath, StringComparison.OrdinalIgnoreCase)))
                    .Where(i => i != null)
                    .OrderBy(i => i.FullPath));
        }

        /// <summary>
        /// Adds the specified solution items.
        /// </summary>
        /// <param name="items">An <see cref="IEnumerable{String}"/> containing items to add to the solution.</param>
        public void AddSolutionItems(IEnumerable<string> items)
        {
            _solutionItems.AddRange(items);
        }

        /// <summary>
        /// Saves the Visual Studio solution to a file.
        /// </summary>
        /// <param name="path">The full path to the file to write to.</param>
        /// <param name="useFolders">Specifies if folders should be created.</param>
        public void Save(string path, bool useFolders)
        {
            string directoryName = Path.GetDirectoryName(path);

            if (!directoryName.IsNullOrWhiteSpace())
            {
                Directory.CreateDirectory(directoryName);
            }

            using (StreamWriter writer = File.CreateText(path))
            {
                Save(writer, useFolders);
            }
        }

        internal void Save(TextWriter writer, bool useFolders)
        {
            writer.WriteLine(Header, _fileFormatVersion);

            if (SolutionItems.Count > 0)
            {
                writer.WriteLine($@"Project(""{SlnFolder.FolderProjectTypeGuid}"") = ""Solution Items"", ""Solution Items"", ""{Guid.NewGuid().ToSolutionString()}"" ");
                writer.WriteLine("	ProjectSection(SolutionItems) = preProject");
                foreach (string solutionItem in SolutionItems)
                {
                    writer.WriteLine($"		{solutionItem} = {solutionItem}");
                }

                writer.WriteLine("	EndProjectSection");
                writer.WriteLine("EndProject");
            }

            foreach (SlnProject project in _projects.OrderBy(i => i.FullPath))
            {
                writer.WriteLine($@"Project(""{project.ProjectTypeGuid.ToSolutionString()}"") = ""{project.Name}"", ""{project.FullPath}"", ""{project.ProjectGuid.ToSolutionString()}""");
                writer.WriteLine("EndProject");
            }

            SlnHierarchy hierarchy = null;

            if (useFolders && _projects.Any(i => !i.IsMainProject))
            {
                hierarchy = new SlnHierarchy(_projects);

                foreach (SlnFolder folder in hierarchy.Folders)
                {
                    writer.WriteLine($@"Project(""{folder.ProjectTypeGuid}"") = ""{folder.Name}"", ""{folder.FullPath}"", ""{folder.FolderGuid.ToSolutionString()}""");
                    writer.WriteLine("EndProject");
                }
            }

            writer.WriteLine("Global");

            if (useFolders && _projects.Count > 1 && hierarchy != null)
            {
                writer.WriteLine(@"	GlobalSection(NestedProjects) = preSolution");

                foreach (SlnFolder folder in hierarchy.Folders.Where(i => i.Parent != null))
                {
                    foreach (SlnProject project in folder.Projects)
                    {
                        writer.WriteLine($@"		{project.ProjectGuid.ToSolutionString()} = {folder.FolderGuid.ToSolutionString()}");
                    }

                    writer.WriteLine($@"		{folder.FolderGuid.ToSolutionString()} = {folder.Parent.FolderGuid.ToSolutionString()}");
                }

                writer.WriteLine("	EndGlobalSection");
            }

            writer.WriteLine("	GlobalSection(SolutionConfigurationPlatforms) = preSolution");

            HashSet<string> allPlatforms = Platforms != null && Platforms.Any()
                ? new HashSet<string>(Platforms)
                : new HashSet<string>(_projects.SelectMany(i => i.Platforms).Select(i => i.ToSolutionPlatform()).Where(i => !i.IsNullOrWhiteSpace() && !string.Equals(i, "Win32", StringComparison.OrdinalIgnoreCase)).OrderBy(i => i), StringComparer.OrdinalIgnoreCase);

            HashSet<string> allConfigurations = Configurations != null && Configurations.Any()
                ? new HashSet<string>(Configurations)
                : new HashSet<string>(_projects.SelectMany(i => i.Configurations).Where(i => !i.IsNullOrWhiteSpace()), StringComparer.OrdinalIgnoreCase);

            foreach (string configuration in allConfigurations)
            {
                foreach (string platform in allPlatforms)
                {
                    if (!string.IsNullOrWhiteSpace(configuration) && !string.IsNullOrWhiteSpace(platform))
                    {
                        writer.WriteLine($"		{configuration}|{platform} = {configuration}|{platform}");
                    }
                }
            }

            writer.WriteLine("	EndGlobalSection");

            writer.WriteLine("	GlobalSection(ProjectConfigurationPlatforms) = postSolution");

            foreach (SlnProject project in _projects)
            {
                string projectGuid = project.ProjectGuid.ToSolutionString();

                foreach (string configuration in allConfigurations)
                {
                    foreach (string platform in allPlatforms)
                    {
                        var foundPlatform = TryGetProjectSolutionPlatform(platform, project, out string projectSolutionPlatform, out string projectBuildPlatform);

                        writer.WriteLine($@"		{projectGuid}.{configuration}|{platform}.ActiveCfg = {configuration}|{projectSolutionPlatform}");

                        if (foundPlatform)
                        {
                            writer.WriteLine($@"		{projectGuid}.{configuration}|{platform}.Build.0 = {configuration}|{projectBuildPlatform}");
                        }

                        if (project.IsDeployable)
                        {
                            writer.WriteLine($@"		{projectGuid}.{configuration}|{platform}.Deploy.0 = {configuration}|{projectSolutionPlatform}");
                        }
                    }
                }
            }

            writer.WriteLine("	EndGlobalSection");

            writer.WriteLine("	GlobalSection(SolutionProperties) = preSolution");
            writer.WriteLine("		HideSolutionNode = FALSE");
            writer.WriteLine("	EndGlobalSection");

            writer.WriteLine("	GlobalSection(ExtensibilityGlobals) = postSolution");
            writer.WriteLine($"		SolutionGuid = {Guid.NewGuid().ToSolutionString()}");
            writer.WriteLine("	EndGlobalSection");

            writer.WriteLine("EndGlobal");
        }

        private bool TryGetProjectSolutionPlatform(string solutionPlatform, SlnProject project, out string projectSolutionPlatform, out string projectBuildPlatform)
        {
            projectSolutionPlatform = null;
            projectBuildPlatform = null;

            foreach (string projectPlatform in project.Platforms)
            {
                if (string.Equals(projectPlatform, solutionPlatform, StringComparison.OrdinalIgnoreCase) || string.Equals(projectPlatform.ToSolutionPlatform(), solutionPlatform, StringComparison.OrdinalIgnoreCase))
                {
                    projectSolutionPlatform = solutionPlatform;

                    projectBuildPlatform = solutionPlatform;

                    return true;
                }

                if (string.Equals(solutionPlatform, "x86", StringComparison.OrdinalIgnoreCase) && string.Equals(projectPlatform, "Win32", StringComparison.OrdinalIgnoreCase))
                {
                    projectSolutionPlatform = projectPlatform;

                    projectBuildPlatform = "x86";

                    return true;
                }
            }

            projectSolutionPlatform = project.Platforms.First().ToSolutionPlatform();

            return false;
        }
    }
}