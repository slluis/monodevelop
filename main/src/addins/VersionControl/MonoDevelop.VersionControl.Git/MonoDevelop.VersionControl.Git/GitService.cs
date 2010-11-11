// 
// GitService.cs
//  
// Author:
//       Lluis Sanchez Gual <lluis@novell.com>
// 
// Copyright (c) 2010 Novell, Inc (http://www.novell.com)
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

using System;
using MonoDevelop.Core;
using MonoDevelop.Ide;
using MonoDevelop.Ide.ProgressMonitoring;

namespace MonoDevelop.VersionControl.Git
{
	public static class GitService
	{
		public static void Push (GitRepository repo)
		{
			PushDialog dlg = new PushDialog (repo);
			if (dlg.Run () == (int) Gtk.ResponseType.Ok) {
				string remote = dlg.SelectedRemote;
				string branch = dlg.SelectedRemoteBranch;
				dlg.Destroy ();
				IProgressMonitor monitor = VersionControlService.GetProgressMonitor (GettextCatalog.GetString ("Pushing changes..."));
				System.Threading.ThreadPool.QueueUserWorkItem (delegate {
					try {
						repo.Push (monitor, remote, branch);
					} catch (Exception ex) {
						monitor.ReportError (ex.Message, ex);
					} finally {
						monitor.Dispose ();
					}
				});
			} else
				dlg.Destroy ();
		}
	
		public static void ShowConfigurationDialog (GitRepository repo)
		{
			GitConfigurationDialog dlg = new GitConfigurationDialog (repo);
			dlg.Run ();
			dlg.Destroy ();
		}
	
		public static void ShowMergeDialog (GitRepository repo)
		{
			MergeDialog dlg = new MergeDialog (repo);
			try {
				if (dlg.Run () == (int) Gtk.ResponseType.Ok) {
					dlg.Hide ();
					using (IProgressMonitor monitor = VersionControlService.GetProgressMonitor (GettextCatalog.GetString ("Merging branch '{0}'...", dlg.SelectedBranch))) {
						repo.Merge (dlg.SelectedBranch, monitor);
					}
				}
			} finally {
				dlg.Destroy ();
			}
		}
		
		public static void SwitchToBranch (GitRepository repo, string branch)
		{
			MessageDialogProgressMonitor monitor = new MessageDialogProgressMonitor (true, false, false, true);
			try {
				IdeApp.Workbench.AutoReloadDocuments = true;
				IdeApp.Workbench.LockGui ();
				System.Threading.ThreadPool.QueueUserWorkItem (delegate {
					try {
						repo.SwitchToBranch (monitor, branch);
					} catch (Exception ex) {
						monitor.ReportError ("Branch switch failed", ex);
					} finally {
						monitor.Dispose ();
					}
				});
				monitor.AsyncOperation.WaitForCompleted ();
			} finally {
				IdeApp.Workbench.AutoReloadDocuments = false;
				IdeApp.Workbench.UnlockGui ();
			}
		}
	}
}

