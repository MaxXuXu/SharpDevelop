﻿// <file>
//     <copyright see="prj:///doc/copyright.txt"/>
//     <license see="prj:///doc/license.txt"/>
//     <author name="Itai Bar-Haim"/>
//     <version>$Revision$</version>
// </file>

using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace ClassDiagram
{
	public class MinusShape : SmallButtonShape
	{
		static Rectangle minus = new Rectangle(2, 4, 6, 2);
		
		static Pen stroke = Pens.Black;
		static Brush fill = Brushes.White;
		
		public override void Draw(Graphics graphics)
		{
			if (graphics == null) return;
			base.Draw(graphics);
			graphics.FillRectangle (fill, minus);
			graphics.DrawRectangle (stroke, minus);
		}
	}
}
