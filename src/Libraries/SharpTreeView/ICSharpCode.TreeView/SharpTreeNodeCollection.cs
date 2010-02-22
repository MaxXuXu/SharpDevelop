﻿// <file>
//     <copyright see="prj:///doc/copyright.txt"/>
//     <license see="prj:///doc/license.txt"/>
//     <author name="Ivan Shumilin"/>
//     <version>$Revision$</version>
// </file>

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace ICSharpCode.TreeView
{
	public class SharpTreeNodeCollection : ObservableCollection<SharpTreeNode>
	{
		public SharpTreeNodeCollection(SharpTreeNode parent)
		{
			Parent = parent;
		}

		public SharpTreeNode Parent { get; private set; }

		protected override void InsertItem(int index, SharpTreeNode node)
		{
			node.Parent = Parent;
			base.InsertItem(index, node);
		}

		protected override void RemoveItem(int index)
		{
			var node = this[index];
			node.Parent = null;
			base.RemoveItem(index);
		}

		protected override void ClearItems()
		{
			foreach (var node in this) {
				node.Parent = null;
			}
			base.ClearItems();
		}

		protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
		{
			base.OnCollectionChanged(e);
			Parent.OnChildrenChanged(e);
		}
	}
}
