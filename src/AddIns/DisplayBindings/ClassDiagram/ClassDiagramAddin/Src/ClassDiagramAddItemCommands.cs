﻿// <file>
//     <copyright see="prj:///doc/copyright.txt"/>
//     <license see="prj:///doc/license.txt"/>
//     <author name="Itai Bar-Haim"/>
//     <version>$Revision$</version>
// </file>

using System;
using System.Windows.Forms;

using ClassDiagram;

using ICSharpCode.Core;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Project;
using ICSharpCode.SharpDevelop.Commands;
using ICSharpCode.SharpDevelop.Dom;

namespace ClassDiagramAddin
{
	public class AddClassCommand : ClassDiagramAddinCommand
	{
		public override void Run()
		{
			// TODO implement AddClassCommand
		}
	}
	
	public class AddAbstractClassCommand : ClassDiagramAddinCommand
	{
		public override void Run()
		{
			// TODO implement AddAbstractClassCommand
		}
	}
	
	public class AddInterfaceCommand : ClassDiagramAddinCommand
	{
		public override void Run()
		{
			// TODO implement AddInterfaceCommand
		}
	}
	
	public class AddDelegateCommand : ClassDiagramAddinCommand
	{
		public override void Run()
		{
			// TODO implement AddDelegateCommand
		}
	}
	
	public class AddEnumCommand : ClassDiagramAddinCommand
	{
		public override void Run()
		{
			// TODO implement AddEnumCommand
		}
	}
	
	public class AddNoteCommand : ClassDiagramAddinCommand
	{
		public override void Run()
		{
			NoteCanvasItem item = new NoteCanvasItem();
			item.Width = 200;
			item.Height = 200;
			item.X = Canvas.LastMouseClickPosition.X;
			item.Y = Canvas.LastMouseClickPosition.Y;
			Canvas.AddCanvasItem(item);
		}
	}
}
