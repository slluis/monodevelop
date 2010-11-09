// MSBuildProjectHandler.cs
//
// Author:
//   Lluis Sanchez Gual <lluis@novell.com>
//
// Copyright (c) 2008 Novell, Inc (http://www.novell.com)
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
//
//

using System;
using System.Xml;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using MonoDevelop.Core;
using MonoDevelop.Projects;
using MonoDevelop.Core.Serialization;
using MonoDevelop.Core.Assemblies;
using MonoDevelop.Projects.Formats.MD1;
using MonoDevelop.Projects.Extensions;
using MonoDevelop.Core.Execution;
using Mono.Addins;
using System.Linq;
using MonoDevelop.Core.Instrumentation;

namespace MonoDevelop.Projects.Formats.MSBuild
{
	public class MSBuildProjectHandler: MSBuildHandler, IResourceHandler, IPathHandler, IAssemblyReferenceHandler
	{
		string fileContent;
		List<string> targetImports = new List<string> ();
		IResourceHandler customResourceHandler;
		List<string> subtypeGuids = new List<string> ();
		const string Unspecified = null;
		RemoteProjectBuilder projectBuilder;
		string lastBuildToolsVersion;
		string lastBuildRuntime;
		ITimeTracker timer;
		bool useXBuild;
		MSBuildVerbosity verbosity;
		
		struct ItemInfo {
			public MSBuildItem Item;
			public bool Added;
		}
		
		protected SolutionEntityItem EntityItem {
			get { return (SolutionEntityItem) Item; }
		}
		
		public System.Collections.Generic.List<string> TargetImports {
			get {
				return targetImports;
			}
			set {
				targetImports = value;
			}
		}

		public void SetCustomResourceHandler (IResourceHandler value)
		{
			customResourceHandler = value;
		}

		public List<string> SubtypeGuids {
			get {
				return subtypeGuids;
			}
		}
		
		public MSBuildProjectHandler ()
		{
		}
		
		public MSBuildProjectHandler (string typeGuid, string import, string itemId)
		{
			Initialize (typeGuid, import, itemId);
		}
		
		internal void Initialize (string typeGuid, string import, string itemId)
		{
			base.Initialize (typeGuid, itemId);
			if (import != null && import.Trim().Length > 0)
				this.targetImports.AddRange (import.Split (':'));
			
			Runtime.SystemAssemblyService.DefaultRuntimeChanged += OnDefaultRuntimeChanged;
			
			//FIXME: Update these when the properties change
			useXBuild = PropertyService.Get ("MonoDevelop.Ide.BuildWithMSBuild", false);
			verbosity = PropertyService.Get ("MonoDevelop.Ide.MSBuildVerbosity", MSBuildVerbosity.Normal);
		}
		
		void OnDefaultRuntimeChanged (object o, EventArgs args)
		{
			// If the default runtime changes, the project builder for this project may change
			// so it has to be created again.
			if (projectBuilder != null) {
				projectBuilder.Dispose ();
				projectBuilder = null;
			}
		}
		
		RemoteProjectBuilder GetProjectBuilder ()
		{
			SolutionEntityItem item = (SolutionEntityItem) Item;
			TargetRuntime runtime = null;
			string toolsVersion;
			if (item is IAssemblyProject) {
				runtime = ((IAssemblyProject) item).TargetRuntime;
				toolsVersion = this.TargetFormat.ToolsVersion;
			}
			else {
				runtime = Runtime.SystemAssemblyService.CurrentRuntime;
				toolsVersion = MSBuildProjectService.DefaultToolsVersion;
			}
			if (projectBuilder == null || lastBuildToolsVersion != toolsVersion || lastBuildRuntime != runtime.Id) {
				if (projectBuilder != null) {
					projectBuilder.Dispose ();
					projectBuilder = null;
				}
				projectBuilder = MSBuildProjectService.GetProjectBuilder (runtime, toolsVersion, item.FileName);
				lastBuildToolsVersion = toolsVersion;
				lastBuildRuntime = runtime.Id;
			}
			return projectBuilder;
		}
		
		public override void Dispose ()
		{
			base.Dispose ();
			if (projectBuilder != null) {
				projectBuilder.Dispose ();
				projectBuilder = null;
			}
			Runtime.SystemAssemblyService.DefaultRuntimeChanged -= OnDefaultRuntimeChanged;
		}
		
		IEnumerable<string> IAssemblyReferenceHandler.GetAssemblyReferences (ConfigurationSelector configuration)
		{
			if (useXBuild) {
				// Get the references list from the msbuild project
				SolutionEntityItem item = (SolutionEntityItem) Item;
				RemoteProjectBuilder builder = GetProjectBuilder ();
				SolutionItemConfiguration configObject = item.GetConfiguration (configuration);
				foreach (string s in builder.GetAssemblyReferences (configObject.Name, configObject.Platform))
					yield return s;
			}
			else {
				DotNetProject item = Item as DotNetProject;
				if (item == null)
					yield break;
				foreach (ProjectReference pref in item.References.Where (pr => pr.ReferenceType != ReferenceType.Project)) {
					foreach (string asm in pref.GetReferencedFileNames (configuration))
						yield return asm;
				}
			}
		}
		
		public override BuildResult RunTarget (IProgressMonitor monitor, string target, ConfigurationSelector configuration)
		{
			if (useXBuild) {
				SolutionEntityItem item = Item as SolutionEntityItem;
				if (item != null) {
					
					SolutionItemConfiguration configObject = item.GetConfiguration (configuration);
				
					LogWriter logWriter = new LogWriter (monitor.Log);
					RemoteProjectBuilder builder = GetProjectBuilder ();
					MSBuildResult[] results = builder.RunTarget (target, configObject.Name, configObject.Platform,
						logWriter, verbosity);
					System.Runtime.Remoting.RemotingServices.Disconnect (logWriter);
					
					BuildResult br = new BuildResult ();
					foreach (MSBuildResult res in results) {
						if (res.IsWarning)
							br.AddWarning (res.File, res.Line, res.Column, res.Code, res.Message);
						else
							br.AddError (res.File, res.Line, res.Column, res.Code, res.Message);
					}
					return br;
				}
			}
			else {
				if (Item is DotNetProject) {
					MD1DotNetProjectHandler handler = new MD1DotNetProjectHandler ((DotNetProject)Item);
					return handler.RunTarget (monitor, target, configuration);
				}
			}
			return null;
		}
		
		public string GetDefaultResourceId (ProjectFile file)
		{
			if (customResourceHandler != null)
				return customResourceHandler.GetDefaultResourceId (file);
			else
				return MSBuildResourceHandler.Instance.GetDefaultResourceId (file);
		}
		
		public string EncodePath (string path, string oldPath)
		{
			string basePath = Path.GetDirectoryName (EntityItem.FileName);
			return FileService.RelativeToAbsolutePath (basePath, path);
		}
		
		public string DecodePath (string path)
		{
			string basePath = Path.GetDirectoryName (EntityItem.FileName);
			return FileService.AbsoluteToRelativePath (basePath, path);
		}

		public SolutionEntityItem Load (IProgressMonitor monitor, string fileName, string language, Type itemClass)
		{
			timer = Counters.ReadMSBuildProject.BeginTiming ();
			
			timer.Trace ("Reading project file");
			MSBuildProject p = new MSBuildProject ();
			fileContent = File.ReadAllText (fileName);
			p.LoadXml (fileContent);
			
			timer.Trace ("Read project guids");
			
			MSBuildPropertyGroup globalGroup = p.GetGlobalPropertyGroup ();
			
			// Avoid crash if there is not global group
			if (globalGroup == null)
				globalGroup = p.AddNewPropertyGroup (false);
			
			string itemGuid = globalGroup.GetPropertyValue ("ProjectGuid");
			string projectTypeGuids = globalGroup.GetPropertyValue ("ProjectTypeGuids");
			string itemType = globalGroup.GetPropertyValue ("ItemType");

			subtypeGuids.Clear ();
			if (projectTypeGuids != null) {
				foreach (string guid in projectTypeGuids.Split (';')) {
					string sguid = guid.Trim ();
					if (sguid.Length > 0 && string.Compare (sguid, TypeGuid, true) != 0)
						subtypeGuids.Add (guid);
				}
			}
			
			try {
				timer.Trace ("Create item instance");
				ProjectExtensionUtil.BeginLoadOperation ();
				Item = CreateSolutionItem (language, projectTypeGuids, itemType, itemClass);
	
				Item.SetItemHandler (this);
				MSBuildProjectService.SetId (Item, itemGuid);
				
				SolutionEntityItem it = (SolutionEntityItem) Item;
				
				it.FileName = fileName;
				it.Name = System.IO.Path.GetFileNameWithoutExtension (fileName);
			
				Load (monitor, p);
				return it;
				
			} finally {
				ProjectExtensionUtil.EndLoadOperation ();
				timer.End ();
			}
		}
		
		SolutionItem CreateSolutionItem (string language, string typeGuids, string itemType, Type itemClass)
		{
			// All the parameters are optional, but at least one must be provided.
			
			SolutionItem item = null;
			
			if (!string.IsNullOrEmpty (typeGuids)) {
				DotNetProjectSubtypeNode st = MSBuildProjectService.GetDotNetProjectSubtype (typeGuids);
				if (st != null) {
					item = st.CreateInstance (language);
					useXBuild = useXBuild || st.UseXBuild;
					st.UpdateImports ((SolutionEntityItem)item, targetImports);
				} else
					throw new InvalidOperationException ("Unknown solution item type.");
			}
			if (item == null && itemClass != null)
				item = (SolutionItem) Activator.CreateInstance (itemClass);
			
			if (item == null && !string.IsNullOrEmpty (language))
				item = new DotNetAssemblyProject (language);
			
			if (item == null) {
				if (string.IsNullOrEmpty (itemType))
					throw new InvalidOperationException ("Unknown solution item type.");
					
				DataType dt = MSBuildProjectService.DataContext.GetConfigurationDataType (itemType);
				if (dt == null)
					throw new InvalidOperationException ("Unknown solution item type: " + itemType);
					
				item = (SolutionItem) Activator.CreateInstance (dt.ValueType);
			}
			
			// Basic initialization
			
			if (item is DotNetProject) {
				DotNetProject p = (DotNetProject) item;
				p.TargetFramework = Services.ProjectService.DefaultTargetFramework;
			}
			return item;
		}
		
		void Load (IProgressMonitor monitor, MSBuildProject msproject)
		{
			timer.Trace ("Initialize serialization");
			
			MSBuildSerializer ser = CreateSerializer ();
			ser.SerializationContext.BaseFile = EntityItem.FileName;
			ser.SerializationContext.ProgressMonitor = monitor;
			
			MSBuildPropertyGroup globalGroup = msproject.GetGlobalPropertyGroup ();
			
			Item.SetItemHandler (this);
			
			DotNetProject dotNetProject = Item as DotNetProject;
			
			string frameworkVersion = null;
			
			// Read all items
			
			timer.Trace ("Read project items");
			
			foreach (MSBuildItem buildItem in msproject.GetAllItems ()) {
				ProjectItem it = ReadItem (ser, buildItem);
				if (it != null) {
					EntityItem.Items.Add (it);
					int i = EntityItem.Items.IndexOf (it);
					if (i != -1 && EntityItem.Items [i] != it && EntityItem.Items [i].Condition == it.Condition)
						EntityItem.Items.RemoveAt (i); // Remove duplicates
				}
			}
			
			timer.Trace ("Read configurations");
			
			if (dotNetProject != null) {
				frameworkVersion = globalGroup.GetPropertyValue ("TargetFrameworkVersion");
				if (frameworkVersion != null && frameworkVersion.StartsWith ("v"))
					frameworkVersion = frameworkVersion.Substring (1);
				if (!string.IsNullOrEmpty (frameworkVersion))
					dotNetProject.TargetFramework = Runtime.SystemAssemblyService.GetTargetFramework (frameworkVersion);
				
				if (dotNetProject.LanguageParameters != null) {
					DataItem data = ReadPropertyGroupMetadata (ser, globalGroup, dotNetProject.LanguageParameters);
					ser.Deserialize (dotNetProject.LanguageParameters, data);
				}
			}
			
			// Read configurations
			
			List<ConfigData> configData = GetConfigData (msproject);
			List<ConfigData> readConfigData = new List<ConfigData> ();
			
			MSBuildPropertyGroup mergedToProjectProperties = ExtractMergedtoprojectProperties (ser, globalGroup, EntityItem.CreateConfiguration ("Dummy"));
			configData.Insert (0, new ConfigData (Unspecified, Unspecified, mergedToProjectProperties));
			
			foreach (ConfigData cgrp in configData) {
				readConfigData.Add (cgrp);

				string conf = cgrp.Config;
				string platform = cgrp.Platform;

				if (platform == Unspecified && conf != Unspecified && !ContainsSpecificPlatformConfiguration (configData, conf))
					platform = string.Empty;

				// It may be a partial configuration
				if (conf == Unspecified || platform == Unspecified)
					continue;
				
				MSBuildPropertyGroup grp = CreateMergedConfiguration (readConfigData, conf, platform);
				SolutionItemConfiguration config = EntityItem.CreateConfiguration (conf);
				
				config.Platform = platform;
				DataItem data = ReadPropertyGroupMetadata (ser, grp, config);
				ser.Deserialize (config, data);
				EntityItem.Configurations.Add (config);
				
				if (config is DotNetProjectConfiguration) {
					DotNetProjectConfiguration dpc = (DotNetProjectConfiguration) config;
					if (dpc.CompilationParameters != null) {
						data = ReadPropertyGroupMetadata (ser, grp, dpc.CompilationParameters);
						ser.Deserialize (dpc.CompilationParameters, data);
					}
				}
			}
			
			// Read extended properties
			
			timer.Trace ("Read extended properties");
			
			DataItem globalData = ReadPropertyGroupMetadata (ser, globalGroup, Item);
			
			string extendedData = msproject.GetProjectExtensions ("MonoDevelop");
			if (!string.IsNullOrEmpty (extendedData)) {
				StringReader sr = new StringReader (extendedData);
				DataItem data = (DataItem) XmlConfigurationReader.DefaultReader.Read (new XmlTextReader (sr));
				globalData.ItemData.AddRange (data.ItemData);
			}
			ser.Deserialize (Item, globalData);

			// Final initializations
			
			timer.Trace ("Final initializations");
			
			if (dotNetProject != null && string.IsNullOrEmpty (frameworkVersion)) {
				string fx = Item.ExtendedProperties ["InternalTargetFrameworkVersion"] as string;
				if (fx != null) {
					dotNetProject.TargetFramework = Runtime.SystemAssemblyService.GetTargetFramework (fx);
					Item.ExtendedProperties.Remove ("InternalTargetFrameworkVersion");
				} else {
					// If no framework is specified, it means the format is VS2005, so the default framework is 2.0.
					dotNetProject.TargetFramework = Runtime.SystemAssemblyService.GetTargetFramework ("2.0");
				}
			}
			
			Item.NeedsReload = false;
		}

		MSBuildPropertyGroup ExtractMergedtoprojectProperties (MSBuildSerializer ser, MSBuildPropertyGroup pgroup, object ob)
		{
			XmlDocument doc = new XmlDocument ();
			MSBuildPropertyGroup res = new MSBuildPropertyGroup (doc.CreateElement ("PropGroup"));
			
			foreach (string propName in GetMergeToProjectProperties (ser, ob)) {
				MSBuildProperty bp = pgroup.GetProperty (propName);
				if (bp != null)
					res.SetPropertyValue (bp.Name, bp.Value);
			}
			return res;
		}
		
		IEnumerable<string> GetMergeToProjectProperties (MSBuildSerializer ser, object ob)
		{
			ClassDataType dt = (ClassDataType) ser.DataContext.GetConfigurationDataType (ob.GetType ());
			foreach (ItemProperty prop in dt.GetProperties (ser.SerializationContext, ob)) {
				if (IsMergeToProjectProperty (prop))
					yield return prop.Name;
			}
		}

		ProjectItem ReadItem (MSBuildSerializer ser, MSBuildItem buildItem)
		{
			Project project = Item as Project;
			DotNetProject dotNetProject = Item as DotNetProject;
			
			DataType dt = ser.DataContext.GetConfigurationDataType (buildItem.Name);
			
			if (project != null) {
				if (buildItem.Name == "Folder") {
					// Read folders
					string path = MSBuildProjectService.FromMSBuildPath (project.ItemDirectory, buildItem.Include);
					return new ProjectFile () { Name = Path.GetDirectoryName (path), Subtype = Subtype.Directory };
				}
				else if (buildItem.Name == "Reference" && dotNetProject != null) {
					ProjectReference pref;
					if (buildItem.HasMetadata ("HintPath")) {
						string hintPath = buildItem.GetMetadata ("HintPath");
						string path;
						if (!MSBuildProjectService.FromMSBuildPath (dotNetProject.ItemDirectory, hintPath, out path)) {
							pref = new ProjectReference (ReferenceType.Assembly, path);
							pref.SetInvalid (GettextCatalog.GetString ("Invalid file path"));
							pref.ExtendedProperties ["_OriginalMSBuildReferenceInclude"] = buildItem.Include;
							pref.ExtendedProperties ["_OriginalMSBuildReferenceHintPath"] = hintPath;
						} else if (File.Exists (path)) {
							pref = new ProjectReference (ReferenceType.Assembly, path);
							if (MSBuildProjectService.IsAbsoluteMSBuildPath (hintPath))
								pref.ExtendedProperties ["_OriginalMSBuildReferenceIsAbsolute"] = true;
						} else {
							pref = new ProjectReference (ReferenceType.Gac, buildItem.Include);
							pref.ExtendedProperties ["_OriginalMSBuildReferenceHintPath"] = hintPath;
						}
						pref.LocalCopy = !buildItem.GetMetadataIsFalse ("Private");
					} else {
						string asm = buildItem.Include;
						// This is a workaround for a VS bug. Looks like it is writing this assembly incorrectly
						if (asm == "System.configuration")
							asm = "System.Configuration";
						else if (asm == "System.XML")
							asm = "System.Xml";
						else if (asm == "system")
							asm = "System";
						pref = new ProjectReference (ReferenceType.Gac, asm);
					}
					pref.Condition = buildItem.Condition;
					pref.SpecificVersion = !buildItem.GetMetadataIsFalse ("SpecificVersion");
					ReadBuildItemMetadata (ser, buildItem, pref, typeof(ProjectReference));
					return pref;
				}
				else if (buildItem.Name == "ProjectReference" && dotNetProject != null) {
					// Get the project name from the path, since the Name attribute may other stuff other than the name
					string path = MSBuildProjectService.FromMSBuildPath (project.ItemDirectory, buildItem.Include);
					string name = Path.GetFileNameWithoutExtension (path);
					ProjectReference pref = new ProjectReference (ReferenceType.Project, name);
					pref.LocalCopy = !buildItem.GetMetadataIsFalse ("Private");
					pref.Condition = buildItem.Condition;
					return pref;
				}
				else if (dt == null && !string.IsNullOrEmpty (buildItem.Include)) {
					// Unknown item. Must be a file.
					if (!UnsupportedItems.Contains (buildItem.Name) && IsValidFile (buildItem.Include))
						return ReadProjectFile (ser, project, buildItem, typeof(ProjectFile));
				}
			}
			
			if (dt != null && typeof(ProjectItem).IsAssignableFrom (dt.ValueType)) {
				ProjectItem obj = (ProjectItem) Activator.CreateInstance (dt.ValueType);
				ReadBuildItemMetadata (ser, buildItem, obj, dt.ValueType);
				return obj;
			}
			
			UnknownProjectItem uitem = new UnknownProjectItem (buildItem.Name, "");
			ReadBuildItemMetadata (ser, buildItem, uitem, typeof(UnknownProjectItem));
			
			return uitem;
		}

		bool IsValidFile (string path)
		{
			// If it is an absolute uri, it's not a valid file
			try {
				return !Uri.IsWellFormedUriString (path, UriKind.Absolute);
			} catch {
				// Old mono versions may crash in IsWellFormedUriString if the path
				// is not an uri.
				return true;
			}
		}
		
		class ConfigData
		{
			public ConfigData (string conf, string plt, MSBuildPropertyGroup grp)
			{
				Config = conf;
				Platform = plt;
				Group = grp;
			}
			
			public string Config;
			public string Platform;
			public MSBuildPropertyGroup Group;
		}

		MSBuildPropertyGroup CreateMergedConfiguration (List<ConfigData> configData, string conf, string platform)
		{
			MSBuildPropertyGroup merged = null;
			
			foreach (ConfigData grp in configData) {
				if ((grp.Config == conf || grp.Config == Unspecified) && (grp.Platform == platform || grp.Platform == Unspecified)) {
					if (merged == null)
						merged = grp.Group;
					else
						merged = MSBuildPropertyGroup.Merge (merged, grp.Group);
				}
			}
			return merged;
		}

		bool ContainsSpecificPlatformConfiguration (List<ConfigData> configData, string conf)
		{
			foreach (ConfigData grp in configData) {
				if (grp.Config == conf && grp.Platform != Unspecified)
					return true;
			}
			return false;
		}

		protected override void SaveItem (MonoDevelop.Core.IProgressMonitor monitor)
		{
			if (Item is UnknownProject || Item is UnknownSolutionItem)
				return;
			
			bool newProject = false;
			SolutionEntityItem eitem = EntityItem;
			
			MSBuildSerializer ser = CreateSerializer ();
			ser.SerializationContext.BaseFile = eitem.FileName;
			ser.SerializationContext.ProgressMonitor = monitor;
			
			DotNetProject dotNetProject = Item as DotNetProject;
			
			MSBuildProject msproject = new MSBuildProject ();
			if (fileContent != null) {
				msproject.LoadXml (fileContent);
			} else {
				msproject.DefaultTargets = "Build";
				newProject = true;
			}

			// Global properties
			
			MSBuildPropertyGroup globalGroup = msproject.GetGlobalPropertyGroup ();
			if (globalGroup == null) {
				globalGroup = msproject.AddNewPropertyGroup (false);
			}
			
			if (eitem.Configurations.Count > 0) {
				ItemConfiguration conf = eitem.Configurations ["Debug"];
				if (conf == null) conf = eitem.Configurations [0];
				MSBuildProperty bprop = SetGroupProperty (globalGroup, "Configuration", conf.Name, false);
				bprop.Condition = " '$(Configuration)' == '' ";
				
				string platform = conf.Platform.Length == 0 ? "AnyCPU" : conf.Platform;
				bprop = SetGroupProperty (globalGroup, "Platform", platform, false);
				bprop.Condition = " '$(Platform)' == '' ";
			}
			
			if (TypeGuid == MSBuildProjectService.GenericItemGuid) {
				DataType dt = MSBuildProjectService.DataContext.GetConfigurationDataType (Item.GetType ());
				SetGroupProperty (globalGroup, "ItemType", dt.Name, false);
			}

			Item.ExtendedProperties ["ProjectGuid"] = Item.ItemId;
			if (subtypeGuids.Count > 0) {
				string gg = "";
				foreach (string sg in subtypeGuids) {
					if (gg.Length > 0)
						gg += ";";
					gg += sg;
				}
				gg += ";" + TypeGuid;
				Item.ExtendedProperties ["ProjectTypeGuids"] = gg.ToUpper ();
			}
			else
				Item.ExtendedProperties.Remove ("ProjectTypeGuids");

			string productVersion = (string) Item.ExtendedProperties ["ProductVersion"];
			if (productVersion == null) {
				Item.ExtendedProperties ["ProductVersion"] = TargetFormat.ProductVersion;
				productVersion = TargetFormat.ProductVersion;
			}

			Item.ExtendedProperties ["SchemaVersion"] = "2.0";
			
			if (TargetFormat.ToolsVersion != "2.0")
				msproject.ToolsVersion = TargetFormat.ToolsVersion;
			else
				msproject.ToolsVersion = string.Empty;

			
			string targetFramework = null;
			IList supportedFrameworks = TargetFormat.FrameworkVersions;
			
			if (dotNetProject != null) {

				targetFramework = dotNetProject.TargetFramework.Id;

				// If the file format does not support this framework version, store the highest version
				// supported in the TargetFrameworkVersion property and the real one in the extended properties
				if (!supportedFrameworks.Contains (targetFramework)) {
					dotNetProject.ExtendedProperties ["InternalTargetFrameworkVersion"] = targetFramework;
					targetFramework = FindClosestSupportedVersion (targetFramework);
				} else
					dotNetProject.ExtendedProperties.Remove ("InternalTargetFrameworkVersion");
			}

			// This serialize call will write data to ser.InternalItemProperties and ser.ExternalItemProperties
			ser.Serialize (Item, Item.GetType ());
			
			object langParams = null;
			
			if (dotNetProject != null && dotNetProject.LanguageParameters != null) {
				// Remove all language parameters properties from the data item, since we are going to write them again.
				ClassDataType dt = (ClassDataType) ser.DataContext.GetConfigurationDataType (dotNetProject.LanguageParameters.GetType ());
				foreach (ItemProperty prop in dt.GetProperties (ser.SerializationContext, dotNetProject.LanguageParameters)) {
					DataNode n = ser.InternalItemProperties.ItemData [prop.Name];
					if (n != null)
						ser.InternalItemProperties.ItemData.Remove (n);
				}
				DataItem ditemComp = (DataItem) ser.Serialize (dotNetProject.LanguageParameters);
				ser.InternalItemProperties.ItemData.AddRange (ditemComp.ItemData);
				langParams = dotNetProject.LanguageParameters;
			}
			
			if (fileContent == null)
				ser.InternalItemProperties.ItemData.Sort (globalConfigOrder);

			WritePropertyGroupMetadata (globalGroup, ser.InternalItemProperties.ItemData, ser, Item, langParams);
			
			// Convert debug property
			
			foreach (SolutionItemConfiguration conf in eitem.Configurations) {
				DotNetProjectConfiguration cp = conf as MonoDevelop.Projects.DotNetProjectConfiguration;
				if (cp != null) {
					if (newProject)
						cp.ExtendedProperties ["ErrorReport"] = "prompt";
					
					string debugType = (string) cp.ExtendedProperties ["DebugType"];
					if (cp.DebugMode) {
						if (debugType != "full" && debugType != "pdbonly")
							cp.ExtendedProperties ["DebugType"] = "full";
					}
					else if (debugType != "none" && debugType != "pdbonly")
						cp.ExtendedProperties ["DebugType"] = "none";
				}
			}
			
			// Configurations

			if (eitem.Configurations.Count > 0) {
				List<ConfigData> configData = GetConfigData (msproject);
				Dictionary<string,string> mergeToProjectProperties = new Dictionary<string,string> ();
				List<string> mergeToProjectPropertyNames = new List<string> (GetMergeToProjectProperties (ser, eitem.Configurations [0]));
				List<string> mergeToProjectPropertyNamesCopy = new List<string> (mergeToProjectPropertyNames);
				
				foreach (SolutionItemConfiguration conf in eitem.Configurations) {
					bool newConf = false;
					MSBuildPropertyGroup propGroup = FindPropertyGroup (configData, conf);
					if (propGroup == null) {
						propGroup = msproject.AddNewPropertyGroup (false);
						propGroup.Condition = BuildConfigCondition (conf.Name, conf.Platform);
						ConfigData cd = new ConfigData (conf.Name, conf.Platform, propGroup);
						configData.Add (cd);
						newConf = true;
					}
					
					DotNetProjectConfiguration netConfig = conf as DotNetProjectConfiguration;
					
					DataItem ditem = (DataItem) ser.Serialize (conf);
					
					if (netConfig != null && netConfig.CompilationParameters != null) {
						// Remove all compilation parameters properties from the data item, since we are going to write them again.
						ClassDataType dt = (ClassDataType) ser.DataContext.GetConfigurationDataType (netConfig.CompilationParameters.GetType ());
						foreach (ItemProperty prop in dt.GetProperties (ser.SerializationContext, netConfig.CompilationParameters)) {
							DataNode n = ditem.ItemData [prop.Name];
							if (n != null)
								ditem.ItemData.Remove (n);
						}
						DataItem ditemComp = (DataItem) ser.Serialize (netConfig.CompilationParameters);
						ditem.ItemData.AddRange (ditemComp.ItemData);
					}
	
					if (newConf)
						ditem.ItemData.Sort (configOrder);
					
					WritePropertyGroupMetadata (propGroup, ditem.ItemData, ser, conf, netConfig != null ? netConfig.CompilationParameters : null);
					
					UnmergeBaseConfiguration (configData, propGroup, conf.Name, conf.Platform);
					
					CollectMergetoprojectProperties (propGroup, mergeToProjectPropertyNames, mergeToProjectProperties);
				}
				
				// Move properties with common values from configurations to the main
				// property group
				foreach (KeyValuePair<string,string> prop in mergeToProjectProperties)
					globalGroup.SetPropertyValue (prop.Key, prop.Value);
				foreach (string prop in mergeToProjectPropertyNamesCopy) {
					if (!mergeToProjectProperties.ContainsKey (prop))
						globalGroup.RemoveProperty (prop);
				}
				foreach (SolutionItemConfiguration conf in eitem.Configurations) {
					MSBuildPropertyGroup propGroup = FindPropertyGroup (configData, conf);
					foreach (string mp in mergeToProjectProperties.Keys)
						propGroup.RemoveProperty (mp);
				}
			}
			
			// Remove old items
			Dictionary<string,ItemInfo> oldItems = new Dictionary<string, ItemInfo> ();
			foreach (MSBuildItem item in msproject.GetAllItems ())
				oldItems [item.Name + "<" + item.Include] = new ItemInfo () { Item=item };
			
			// Add the new items
			foreach (object ob in ((SolutionEntityItem)Item).Items)
				SaveItem (monitor, ser, msproject, ob, oldItems);
			
			foreach (ItemInfo itemInfo in oldItems.Values) {
				if (!itemInfo.Added)
					msproject.RemoveItem (itemInfo.Item);
			}
		
			if (dotNetProject != null) {

				// If the format only supports one fx version, there is no need to store it
				if (supportedFrameworks.Count > 1)
					SetGroupProperty (globalGroup, "TargetFrameworkVersion", "v" + targetFramework, false);
				else
					globalGroup.RemoveProperty ("TargetFrameworkVersion");
				
			}

			// Impdate the imports section
			
			List<string> currentImports = msproject.Imports;
			List<string> imports = new List<string> (currentImports);
			UpdateImports (imports);
			foreach (string imp in imports) {
				if (!currentImports.Contains (imp)) {
					msproject.AddNewImport (imp, null);
					currentImports.Add (imp);
				}
			}
			foreach (string imp in currentImports) {
				if (!imports.Contains (imp))
					msproject.RemoveImport (imp);
			}
			
			DataItem extendedData = ser.ExternalItemProperties;
			if (extendedData.HasItemData) {
				extendedData.Name = "Properties";
				StringWriter sw = new StringWriter ();
				XmlConfigurationWriter.DefaultWriter.Write (new XmlTextWriter (sw), extendedData);
				msproject.SetProjectExtensions ("MonoDevelop", sw.ToString ());
			} else
				msproject.RemoveProjectExtensions ("MonoDevelop");
			
			string txt = msproject.Save ();
			
			// Don't save the file to disk if the content did not change
			if (txt != fileContent) {
				File.WriteAllText (eitem.FileName, txt);
				fileContent = txt;
				
				if (projectBuilder != null)
					projectBuilder.Refresh ();
			}
		}

		void CollectMergetoprojectProperties (MSBuildPropertyGroup pgroup, List<String> propertyNames, Dictionary<string,string> mergeToProjectProperties)
		{
			// This method checks every property in pgroup which has the MergeToProject flag.
			// If the value of this property is the same as the one stored in mergeToProjectProperties
			// it means that the property can be merged to the main project property group (so far).
			
			foreach (string pname in new List<String> (propertyNames)) {
				MSBuildProperty prop = pgroup.GetProperty (pname);
				
				string mvalue;
				if (!mergeToProjectProperties.TryGetValue (pname, out mvalue)) {
					if (prop != null) {
						// This is the first time the value is checked. Just assign it.
						mergeToProjectProperties.Add (pname, prop.Value);
						continue;
					}
					// If there is no value, it can't be merged
				}
				else if (prop != null && prop.Value == mvalue)
					// Same value. It can be merged.
					continue;

				// The property can't be merged because different configurations have different
				// values for it. Remove it from the list.
				propertyNames.Remove (pname);
				mergeToProjectProperties.Remove (pname);
			}
		}

		
		void SaveItem (MonoDevelop.Core.IProgressMonitor monitor, MSBuildSerializer ser, MSBuildProject msproject, object ob, Dictionary<string,ItemInfo> oldItems)
		{
			if (ob is ProjectReference) {
				SaveReference (monitor, ser, msproject, (ProjectReference) ob, oldItems);
			}
			else if (ob is ProjectFile) {
				SaveProjectFile (ser, msproject, (ProjectFile) ob, oldItems);
			}
			else {
				string itemName;
				if (ob is UnknownProjectItem)
					itemName = ((UnknownProjectItem)ob).ItemName;
				else {
					DataType dt = ser.DataContext.GetConfigurationDataType (ob.GetType ());
					itemName = dt.Name;
				}
				MSBuildItem buildItem = msproject.AddNewItem (itemName, "");
				WriteBuildItemMetadata (ser, buildItem, ob, oldItems);
			}
		}
		
		void SaveProjectFile (MSBuildSerializer ser, MSBuildProject msproject, ProjectFile file, Dictionary<string,ItemInfo> oldItems)
		{
			string itemName = (file.Subtype == Subtype.Directory)? "Folder" : file.BuildAction;

			string path = MSBuildProjectService.ToMSBuildPath (Item.ItemDirectory, file.FilePath);
			if (path.Length == 0)
				return;
			
			//directory paths must end with '/'
			if ((file.Subtype == Subtype.Directory) && path[path.Length-1] != '\\')
				path = path + "\\";
			
			MSBuildItem buildItem = AddOrGetBuildItem (msproject, oldItems, itemName, path);
			WriteBuildItemMetadata (ser, buildItem, file, oldItems);
			
			if (!string.IsNullOrEmpty (file.DependsOn))
				buildItem.SetMetadata ("DependentUpon", MSBuildProjectService.ToMSBuildPath (Path.GetDirectoryName (file.FilePath), file.DependsOn));
			if (!string.IsNullOrEmpty (file.ContentType))
				buildItem.SetMetadata ("SubType", file.ContentType);
			
			if (!string.IsNullOrEmpty (file.Generator))
				buildItem.SetMetadata ("Generator", file.Generator);
			else
				buildItem.UnsetMetadata ("Generator");
			
			if (!string.IsNullOrEmpty (file.CustomToolNamespace))
				buildItem.SetMetadata ("CustomToolNamespace", file.CustomToolNamespace);
			else
				buildItem.UnsetMetadata ("CustomToolNamespace");
			
			if (!string.IsNullOrEmpty (file.LastGenOutput))
				buildItem.SetMetadata ("LastGenOutput", file.LastGenOutput);
			else
				buildItem.UnsetMetadata ("LastGenOutput");
			
			if (!string.IsNullOrEmpty (file.Link))
				buildItem.SetMetadata ("Link", MSBuildProjectService.ToMSBuildPathRelative (Item.ItemDirectory, file.Link));
			else
				buildItem.UnsetMetadata ("Link");
			
			buildItem.Condition = file.Condition;
			
			if (file.CopyToOutputDirectory == FileCopyMode.None) {
				buildItem.UnsetMetadata ("CopyToOutputDirectory");
			} else {
				buildItem.SetMetadata ("CopyToOutputDirectory", file.CopyToOutputDirectory.ToString ());
			}
			
			if (!file.Visible) {
				buildItem.SetMetadata ("Visible", "False");
			} else {
				buildItem.UnsetMetadata ("Visible");
			}
			
			if (file.BuildAction == BuildAction.EmbeddedResource) {
				//Emit LogicalName only when it does not match the default Id
				if (GetDefaultResourceId (file) != file.ResourceId)
					buildItem.SetMetadata ("LogicalName", file.ResourceId);
			}
		}
		
		void SaveReference (MonoDevelop.Core.IProgressMonitor monitor, MSBuildSerializer ser, MSBuildProject msproject, ProjectReference pref, Dictionary<string,ItemInfo> oldItems)
		{
			MSBuildItem buildItem;
			if (pref.ReferenceType == ReferenceType.Assembly) {
				string asm = null;
				string hintPath = null;
				if (pref.ExtendedProperties.Contains ("_OriginalMSBuildReferenceInclude")) {
					asm = (string) pref.ExtendedProperties ["_OriginalMSBuildReferenceInclude"];
					hintPath = (string) pref.ExtendedProperties ["_OriginalMSBuildReferenceHintPath"];
				}
				else {
					if (File.Exists (pref.Reference)) {
						try {
							asm = AssemblyName.GetAssemblyName (pref.Reference).FullName;
						} catch (Exception ex) {
							string msg = string.Format ("Could not get full name for assembly '{0}'.", pref.Reference);
							monitor.ReportWarning (msg);
							LoggingService.LogError (msg, ex);
						}
					}
					string basePath = Item.ItemDirectory;
					if (pref.ExtendedProperties.Contains ("_OriginalMSBuildReferenceIsAbsolute"))
						basePath = null;
					hintPath = MSBuildProjectService.ToMSBuildPath (basePath, pref.Reference);
				}
				if (asm == null)
					asm = Path.GetFileNameWithoutExtension (pref.Reference);
				
				buildItem = AddOrGetBuildItem (msproject, oldItems, "Reference", asm);
				if (!pref.SpecificVersion)
					buildItem.SetMetadata ("SpecificVersion", "False");
				else
					buildItem.UnsetMetadata ("SpecificVersion");
				buildItem.SetMetadata ("HintPath", hintPath);
				if (!pref.LocalCopy)
					buildItem.SetMetadata ("Private", "False");
				else
					buildItem.UnsetMetadata ("Private");
			}
			else if (pref.ReferenceType == ReferenceType.Gac) {
				string include = pref.StoredReference;
				SystemPackage pkg = pref.Package;
				if (pkg != null && pkg.IsFrameworkPackage) {
					int i = include.IndexOf (',');
					if (i != -1)
						include = include.Substring (0, i).Trim ();
				}
				buildItem = AddOrGetBuildItem (msproject, oldItems, "Reference", include);
				if (!pref.SpecificVersion)
					buildItem.SetMetadata ("SpecificVersion", "False");
				else
					buildItem.UnsetMetadata ("SpecificVersion");
				IList supportedFrameworks = TargetFormat.FrameworkVersions;
				if (pkg != null && pkg.IsFrameworkPackage && supportedFrameworks.Contains (pkg.TargetFramework) && pkg.TargetFramework != "2.0" && supportedFrameworks.Count > 1) {
					TargetFramework fx = Runtime.SystemAssemblyService.GetTargetFramework (pkg.TargetFramework);
					buildItem.SetMetadata ("RequiredTargetFramework", fx.Id);
				} else
					buildItem.UnsetMetadata ("RequiredTargetFramework");
				string hintPath = (string) pref.ExtendedProperties ["_OriginalMSBuildReferenceHintPath"];
				if (hintPath != null)
					buildItem.SetMetadata ("HintPath", hintPath);
				else
					buildItem.UnsetMetadata ("HintPath");
			}
			else if (pref.ReferenceType == ReferenceType.Project) {
				Project refProj = Item.ParentSolution.FindProjectByName (pref.Reference);
				if (refProj != null) {
					buildItem = AddOrGetBuildItem (msproject, oldItems, "ProjectReference", MSBuildProjectService.ToMSBuildPath (Item.ItemDirectory, refProj.FileName));
					MSBuildProjectHandler handler = refProj.ItemHandler as MSBuildProjectHandler;
					if (handler != null)
						buildItem.SetMetadata ("Project", handler.Item.ItemId);
					else
						buildItem.UnsetMetadata ("Project");
					buildItem.SetMetadata ("Name", refProj.Name);
					if (!pref.LocalCopy)
						buildItem.SetMetadata ("Private", "False");
					else
						buildItem.UnsetMetadata ("Private");
				} else {
					monitor.ReportWarning (GettextCatalog.GetString ("Reference to unknown project '{0}' ignored.", pref.Reference));
					return;
				}
			}
			else {
				// Custom
				DataType dt = ser.DataContext.GetConfigurationDataType (pref.GetType ());
				buildItem = AddOrGetBuildItem (msproject, oldItems, dt.Name, pref.Reference);
			}
			WriteBuildItemMetadata (ser, buildItem, pref, oldItems);
			buildItem.Condition = pref.Condition;
		}
		
		void UpdateImports (List<string> imports)
		{
			if (targetImports != null) {
				foreach (string imp in targetImports)
					if (!imports.Contains (imp))
						imports.Add (imp);
			}
			foreach (IMSBuildImportProvider ip in AddinManager.GetExtensionObjects ("/MonoDevelop/ProjectModel/MSBuildImportProviders")) {
				ip.UpdateImports (EntityItem, imports);
			}
		}

		void UnmergeBaseConfiguration (List<ConfigData> configData, MSBuildPropertyGroup propGroup, string conf, string platform)
		{
			MSBuildPropertyGroup baseGroup = null;
			
			foreach (ConfigData data in configData) {
				if (data.Group == propGroup)
					break;
				if ((data.Config == conf || data.Config == Unspecified) && (data.Platform == platform || data.Platform == Unspecified)) {
					if (baseGroup == null)
						baseGroup = data.Group;
					else
						baseGroup = MSBuildPropertyGroup.Merge (baseGroup, data.Group);
				}
			}
			if (baseGroup != null)
				propGroup.UnMerge (baseGroup);
		}
		
		void ReadBuildItemMetadata (DataSerializer ser, MSBuildItem buildItem, object dataItem, Type extendedType)
		{
			DataItem ditem = new DataItem ();
			foreach (ItemProperty prop in ser.GetProperties (dataItem)) {
				string name = ToMsbuildItemName (prop.Name);
				if (name == "Include")
					ditem.ItemData.Add (new DataValue ("Include", buildItem.Include));
				else if (buildItem.HasMetadata (name)) {
					string data = buildItem.GetMetadata (name);
					ditem.ItemData.Add (GetDataNode (prop, data));
				}
			}
			ConvertFromMsbuildFormat (ditem);
			ser.Deserialize (dataItem, ditem);
		}
		
		void WriteBuildItemMetadata (DataSerializer ser, MSBuildItem buildItem, object dataItem, Dictionary<string,ItemInfo> oldItems)
		{
			var notWrittenProps = new HashSet<string> ();
			foreach (ItemProperty prop in ser.GetProperties (dataItem))
				notWrittenProps.Add (prop.Name);
			
			DataItem ditem = (DataItem) ser.Serialize (dataItem, dataItem.GetType ());
			if (ditem.HasItemData) {
				foreach (DataNode node in ditem.ItemData) {
					notWrittenProps.Remove (node.Name);
					if (node.Name == "Include" && node is DataValue)
						buildItem.Include = ((DataValue) node).Value;
					else {
						ConvertToMsbuildFormat (node);
						buildItem.SetMetadata (node.Name, GetXmlString (node), node is DataItem);
					}
				}
			}
			foreach (string prop in notWrittenProps)
				buildItem.UnsetMetadata (prop);
		}
		
		MSBuildItem AddOrGetBuildItem (MSBuildProject msproject, Dictionary<string,ItemInfo> oldItems, string name, string include)
		{
			ItemInfo itemInfo;
			string key = name + "<" + include;
			if (oldItems.TryGetValue (key, out itemInfo)) {
				if (!itemInfo.Added) {
					itemInfo.Added = true;
					oldItems [key] = itemInfo;
				}
				return itemInfo.Item;
			} else
				return msproject.AddNewItem (name, include);
		}
		
		DataItem ReadPropertyGroupMetadata (DataSerializer ser, MSBuildPropertyGroup propGroup, object dataItem)
		{
			DataItem ditem = new DataItem ();

			foreach (MSBuildProperty bprop in propGroup.Properties) {
				DataNode node = null;
				foreach (XmlNode xnode in bprop.Element.ChildNodes) {
					if (xnode is XmlElement) {
						node = XmlConfigurationReader.DefaultReader.Read ((XmlElement)xnode);
						break;
					}
				}
				if (node == null)
					node = new DataValue (bprop.Name, bprop.Value);
				
				ConvertFromMsbuildFormat (node);
				ditem.ItemData.Add (node);
			}
			
			return ditem;
		}
		
		void WritePropertyGroupMetadata (MSBuildPropertyGroup propGroup, DataCollection itemData, MSBuildSerializer ser, params object[] itemsToReplace)
		{
			var notWrittenProps = new HashSet<string> ();
			
			foreach (object ob in itemsToReplace) {
				if (ob == null)
					continue;
				ClassDataType dt = (ClassDataType) ser.DataContext.GetConfigurationDataType (ob.GetType ());
				foreach (ItemProperty prop in dt.GetProperties (ser.SerializationContext, ob))
					notWrittenProps.Add (prop.Name);
			}
	
			foreach (DataNode node in itemData) {
				notWrittenProps.Remove (node.Name);
				ConvertToMsbuildFormat (node);
				SetGroupProperty (propGroup, node.Name, GetXmlString (node), node is DataItem);
			}
			foreach (string prop in notWrittenProps)
				propGroup.RemoveProperty (prop);
		}
		
		string ToMsbuildItemName (string name)
		{
			return name.Replace ('.', '-');
		}

		void ConvertToMsbuildFormat (DataNode node)
		{
			ReplaceChar (node, true, '.', '-');
		}
		
		void ConvertFromMsbuildFormat (DataNode node)
		{
			ReplaceChar (node, true, '-', '.');
		}
		
		void ReplaceChar (DataNode node, bool force, char oldChar, char newChar)
		{
			DataItem it = node as DataItem;
			if ((force || it != null) && node.Name != null)
				node.Name = node.Name.Replace (oldChar, newChar);
			if (it != null) {
				foreach (DataNode cnode in it.ItemData)
					ReplaceChar (cnode, !it.UniqueNames, oldChar, newChar);
			}
		}

		List<ConfigData> GetConfigData (MSBuildProject msproject)
		{
			List<ConfigData> configData = new List<ConfigData> ();
			foreach (MSBuildPropertyGroup cgrp in msproject.PropertyGroups) {
				string conf, platform;
				if (ParseConfigCondition (cgrp.Condition, out conf, out platform))
					configData.Add (new ConfigData (conf, platform, cgrp));
			}
			return configData;
		}
		
		MSBuildProperty SetGroupProperty (MSBuildPropertyGroup propGroup, string name, string value, bool isLiteral)
		{
			propGroup.SetPropertyValue (name, value);
			return propGroup.GetProperty (name);
		}
		
		MSBuildPropertyGroup FindPropertyGroup (List<ConfigData> configData, SolutionItemConfiguration config)
		{
			foreach (ConfigData data in configData) {
				if (data.Config == config.Name && data.Platform == config.Platform)
					return data.Group;
			}
			return null;
		}
		
		ProjectFile ReadProjectFile (DataSerializer ser, Project project, MSBuildItem buildItem, Type type)
		{
			string path = MSBuildProjectService.FromMSBuildPath (project.ItemDirectory, buildItem.Include);
			ProjectFile file = (ProjectFile) Activator.CreateInstance (type);
			file.Name = path;
			file.BuildAction = buildItem.Name;
			
			ReadBuildItemMetadata (ser, buildItem, file, type);
			
			string dependentFile = buildItem.GetMetadata ("DependentUpon");
			if (!string.IsNullOrEmpty (dependentFile)) {
				dependentFile = MSBuildProjectService.FromMSBuildPath (Path.GetDirectoryName (path), dependentFile);
				file.DependsOn = dependentFile;
			}
			
			string copyToOutputDirectory = buildItem.GetMetadata ("CopyToOutputDirectory");
			if (!string.IsNullOrEmpty (copyToOutputDirectory)) {
				switch (copyToOutputDirectory) {
				case "None": break;
				case "Always": file.CopyToOutputDirectory = FileCopyMode.Always; break;
				case "PreserveNewest": file.CopyToOutputDirectory = FileCopyMode.PreserveNewest; break;
				default:
					MonoDevelop.Core.LoggingService.LogWarning (
						"Unrecognised value {0} for CopyToOutputDirectory MSBuild property",
						copyToOutputDirectory);
					break;
				}
			}
			
			if (buildItem.GetMetadataIsFalse ("Visible"))
				file.Visible = false;
				
			
			string resourceId = buildItem.GetMetadata ("LogicalName");
			if (!string.IsNullOrEmpty (resourceId))
				file.ResourceId = resourceId;
			
			string contentType = buildItem.GetMetadata ("SubType");
			if (!string.IsNullOrEmpty (contentType))
				file.ContentType = contentType;
			
			string generator = buildItem.GetMetadata ("Generator");
			if (!string.IsNullOrEmpty (generator))
				file.Generator = generator;
			
			string customToolNamespace = buildItem.GetMetadata ("CustomToolNamespace");
			if (!string.IsNullOrEmpty (customToolNamespace))
				file.CustomToolNamespace = customToolNamespace;
			
			string lastGenOutput = buildItem.GetMetadata ("LastGenOutput");
			if (!string.IsNullOrEmpty (lastGenOutput))
				file.LastGenOutput = lastGenOutput;
			
			string link = buildItem.GetMetadata ("Link");
			if (!string.IsNullOrEmpty (link))
				file.Link = MSBuildProjectService.FromMSBuildPathRelative (project.ItemDirectory, link);
			
			file.Condition = buildItem.Condition;
			return file;
		}

		bool ParseConfigCondition (string cond, out string config, out string platform)
		{
			config = platform = null;
			int i = cond.IndexOf ("==");
			if (i == -1)
				return false;
			if (cond.Substring (0, i).Trim () == "'$(Configuration)|$(Platform)'") {
				cond = cond.Substring (i+2).Trim (' ','\'');
				i = cond.IndexOf ('|');
				if (i != -1) {
					config = cond.Substring (0, i);
					platform = cond.Substring (i+1);
				} else {
					// Invalid configuration
					return false;
				}
				if (platform == "AnyCPU")
					platform = string.Empty;
				return true;
			}
			else if (cond.Substring (0, i).Trim () == "'$(Configuration)'") {
				config = cond.Substring (i+2).Trim (' ','\'');
				platform = Unspecified;
				return true;
			}
			else if (cond.Substring (0, i).Trim () == "'$(Platform)'") {
				config = Unspecified;
				platform = cond.Substring (i+2).Trim (' ','\'');
				if (platform == "AnyCPU")
					platform = string.Empty;
				return true;
			}
			return false;
		}
		
		string BuildConfigCondition (string config, string platform)
		{
			if (platform.Length == 0)
				platform = "AnyCPU";
			return " '$(Configuration)|$(Platform)' == '" + config + "|" + platform + "' ";
		}

		bool IsMergeToProjectProperty (ItemProperty prop)
		{
			foreach (object at in prop.CustomAttributes) {
				if (at is MergeToProjectAttribute)
					return true;
			}
			return false;
		}

		string FindClosestSupportedVersion (string version)
		{
			TargetFramework sfx = Runtime.SystemAssemblyService.GetTargetFramework (version);
			if (!string.IsNullOrEmpty (sfx.SubsetOfFramework) && ((IList)TargetFormat.FrameworkVersions).Contains (sfx.SubsetOfFramework))
				return sfx.SubsetOfFramework;
			
			foreach (string supv in TargetFormat.FrameworkVersions) {
				TargetFramework fx = Runtime.SystemAssemblyService.GetTargetFramework (supv);
				if (fx.IsCompatibleWithFramework (version))
					return fx.Id;
			}
			return TargetFormat.FrameworkVersions [TargetFormat.FrameworkVersions.Length - 1];
		}
		
		string GetXmlString (DataNode node)
		{
			if (node is DataValue)
				return ((DataValue)node).Value;
			else {
				StringWriter sw = new StringWriter ();
				XmlTextWriter xw = new XmlTextWriter (sw);
				XmlConfigurationWriter.DefaultWriter.Write (xw, node);
				return sw.ToString ();
			}
		}
		
		DataNode GetDataNode (ItemProperty prop, string xmlString)
		{
			if (prop.DataType.IsSimpleType)
				return new DataValue (prop.Name, xmlString);
			else {
				StringReader sr = new StringReader (xmlString);
				return XmlConfigurationReader.DefaultReader.Read (new XmlTextReader (sr));
			}
		}
		
		internal virtual MSBuildSerializer CreateSerializer ()
		{
			return new MSBuildSerializer (EntityItem.FileName);
		}
		
		static readonly MSBuildElementOrder globalConfigOrder = new MSBuildElementOrder (
			"Configuration","Platform","ProductVersion","SchemaVersion","ProjectGuid","ProjectTypeGuids", "OutputType",
		    "AppDesignerFolder","RootNamespace","AssemblyName","StartupObject"
		);
		static readonly MSBuildElementOrder configOrder = new MSBuildElementOrder (
			"DebugSymbols","DebugType","Optimize","OutputPath","DefineConstants","ErrorReport","WarningLevel",
		    "TreatWarningsAsErrors","DocumentationFile"
		);

		// Those are properties which are dynamically set by this file format
		
		internal static readonly ItemMember[] ExtendedMSBuildProperties = new ItemMember [] {
			new ItemMember (typeof(SolutionEntityItem), "ProductVersion"),
			new ItemMember (typeof(SolutionEntityItem), "SchemaVersion"),
			new ItemMember (typeof(SolutionEntityItem), "ProjectGuid"),
			new ItemMember (typeof(SolutionEntityItem), "ProjectTypeGuids"),
			new ItemMember (typeof(DotNetProjectConfiguration), "DebugType"),
			new ItemMember (typeof(DotNetProjectConfiguration), "ErrorReport"),
			new ItemMember (typeof(DotNetProjectConfiguration), "TargetFrameworkVersion"),
			new ItemMember (typeof(ProjectReference), "RequiredTargetFramework"),
			new ItemMember (typeof(Project), "InternalTargetFrameworkVersion", true),
		};
		
		// Items generated by VS but which MD is not using and should be ignored
		
		internal static readonly IList<string> UnsupportedItems = new string[] {
			"BootstrapperFile", "AppDesigner", "WebReferences", "WebReferenceUrl", "Service"
		};
	}
	
	class MSBuildSerializer: DataSerializer
	{
		public DataItem InternalItemProperties = new DataItem ();
		public DataItem ExternalItemProperties = new DataItem ();
		
		public MSBuildSerializer (string baseFile): base (MSBuildProjectService.DataContext)
		{
			// Use windows separators
			SerializationContext.BaseFile = baseFile;
			SerializationContext.DirectorySeparatorChar = '\\';
		}
		
		internal protected override bool CanHandleProperty (ItemProperty prop, SerializationContext serCtx, object instance)
		{
			if (instance is Project) {
				if (prop.Name == "Contents")
					return false;
			}
			if (instance is DotNetProject) {
				if (prop.Name == "References" || prop.Name == "LanguageParameters")
					return false;
			}
			if (instance is SolutionEntityItem) {
				if (prop.IsExtendedProperty (typeof(SolutionEntityItem)))
					return true;
				return prop.Name != "name" && prop.Name != "Configurations";
			}
			if (instance is SolutionFolder) {
				if (prop.Name == "Files")
					return false;
			}
			if (instance is ProjectFile)
				return prop.IsExtendedProperty (typeof(ProjectFile));
			if (instance is ProjectReference)
				return prop.IsExtendedProperty (typeof(ProjectReference)) || prop.Name == "Package";
			if (instance is DotNetProjectConfiguration)
				if (prop.Name == "CodeGeneration")
					return false;
			if (instance is ItemConfiguration)
				if (prop.Name == "name")
					return false;
			return true;
		}
		
		internal protected override DataNode OnSerializeProperty (ItemProperty prop, SerializationContext serCtx, object instance, object value)
		{
			DataNode data = base.OnSerializeProperty (prop, serCtx, instance, value);
			if (instance is SolutionEntityItem && data != null) {
				if (prop.IsExternal)
					ExternalItemProperties.ItemData.Add (data);
				else
					InternalItemProperties.ItemData.Add (data);
			}
			return data;
		}
	}
	
	class MSBuildElementOrder: Dictionary<string, int>
	{
		public MSBuildElementOrder (params string[] elements)
		{
			for (int n=0; n<elements.Length; n++)
				this [elements [n]] = n;
		}
	}
}
