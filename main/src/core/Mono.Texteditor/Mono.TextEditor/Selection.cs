using System;
using Mono.TextEditor.Highlighting;

namespace Mono.TextEditor
{
	public class Selection
	{
		DocumentLocation anchor;
		public DocumentLocation Anchor {
			get {
				return anchor;
			}
			set {
				if (anchor != value) {
					anchor = value;
					OnChanged ();
					IsDirty = false;
				}
			}
		}
		
		DocumentLocation lead;
		public DocumentLocation Lead {
			get {
				return lead;
			}
			set {
				if (lead != value) {
					lead = value;
					OnChanged ();
					IsDirty = false;
				}
			}
		}
		
		public int MinLine {
			get {
				return System.Math.Min (Anchor.Line, Lead.Line);
			}
		}
		
		public int MaxLine {
			get {
				return System.Math.Max (Anchor.Line, Lead.Line);
			}
		}
		
		public SelectionMode SelectionMode {
			get;
			set;
		}
		
		public bool IsDirty {
			get;
			set;
		}
		
		public bool Contains (DocumentLocation loc)
		{
			return anchor <= loc && loc <= lead || lead <= loc && loc <= anchor;
		}
		
		public Selection ()
		{
			SelectionMode = SelectionMode.Normal;
		}
		
		public static Selection Clone (Selection selection)
		{
			if (selection == null)
				return null;
			return new Selection (selection.Anchor, selection.Lead, selection.SelectionMode);
		}
		
		public Selection (int anchorLine, int anchorColumn, int leadLine, int leadColumn) : this(new DocumentLocation (anchorLine, anchorColumn), new DocumentLocation (leadLine, leadColumn), SelectionMode.Normal)
		{
		}
		public Selection (DocumentLocation anchor, DocumentLocation lead) : this (anchor, lead, SelectionMode.Normal)
		{
		}
		
		public Selection (DocumentLocation anchor, DocumentLocation lead, SelectionMode selectionMode)
		{
			if (anchor.Line < DocumentLocation.MinLine || anchor.Column < DocumentLocation.MinColumn)
				throw new ArgumentException ("anchor");
			if (lead.Line < DocumentLocation.MinLine || lead.Column < DocumentLocation.MinColumn)
				throw new ArgumentException ("lead");
			this.Anchor        = anchor;
			this.Lead          = lead;
			this.SelectionMode = selectionMode;
		}
		
		public ISegment GetSelectionRange (TextEditorData data)
		{
			int anchorOffset = GetAnchorOffset (data);
			int leadOffset   = GetLeadOffset (data);
			return new Segment (System.Math.Min (anchorOffset, leadOffset), System.Math.Abs (anchorOffset - leadOffset));
		}
		
		// for markup syntax mode the syntax highlighting information need to be taken into account
		// when calculating the selection offsets.
		int PosToOffset (TextEditorData data, DocumentLocation loc) 
		{
			LineSegment line = data.GetLine (loc.Line);
			if (line == null)
				return 0;
			Chunk startChunk = data.Document.SyntaxMode.GetChunks (data.Document, data.Parent.ColorStyle, line, line.Offset, line.Length);
			int col = 1;
			for (Chunk chunk = startChunk; chunk != null; chunk = chunk != null ? chunk.Next : null) {
				if (col <= loc.Column && loc.Column < col + chunk.Length)
					return chunk.Offset - col + loc.Column;
				col += chunk.Length;
			}
			return line.Offset + line.EditableLength;
		}
		
		public int GetAnchorOffset (TextEditorData data)
		{
			if (data.Document.SyntaxMode is MarkupSyntaxMode)
				return PosToOffset (data, Anchor);
			return data.Document.LocationToOffset (Anchor);
		}
		
		public int GetLeadOffset (TextEditorData data)
		{
			if (data.Document.SyntaxMode is MarkupSyntaxMode)
				return PosToOffset (data, Lead);
			return data.Document.LocationToOffset (Lead);
		}
		
		public override bool Equals (object obj)
		{
			if (obj == null)
				return false;
			if (ReferenceEquals (this, obj))
				return true;
			if (obj.GetType () != typeof(Selection))
				return false;
			Mono.TextEditor.Selection other = (Mono.TextEditor.Selection)obj;
			return Anchor == other.Anchor && Lead == other.Lead;
		}

		public override int GetHashCode ()
		{
			unchecked {
				return Anchor.GetHashCode () ^ Lead.GetHashCode ();
			}
		}
		
		public override string ToString ()
		{
			return string.Format("[Selection: Anchor={0}, Lead={1}, MinLine={2}, MaxLine={3}, SelectionMode={4}]", Anchor, Lead, MinLine, MaxLine, SelectionMode);
		}
		
		protected virtual void OnChanged ()
		{
			if (Changed != null)
				Changed (this, EventArgs.Empty);
		}
		
		public event EventHandler Changed;
	}
}
