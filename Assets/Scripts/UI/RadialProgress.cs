using UnityEngine;
using UnityEngine.UIElements;

[UxmlElement]
public partial class RadialProgress : VisualElement
{
	private float progress;
	private float thickness;
	private float radius;
	private Color color;

	[UxmlAttribute]
	public float Progress
	{
		get => progress;
		set
		{
			progress = Mathf.Clamp01(value);
			MarkDirtyRepaint();
		}
	}
	
	[UxmlAttribute]
	public Color RadialColor
	{
		get => color;
		set
		{
			color = value;
			MarkDirtyRepaint();
		}
	}
	
	[UxmlAttribute]
	public float Thickness 
	{
		get => thickness;
		set
		{
			thickness = Mathf.Clamp(value, 0f, Mathf.Infinity);
			MarkDirtyRepaint();
		}
	}
	
	[UxmlAttribute]
	public float Radius 
	{
		get => radius;
		set
		{
			radius = Mathf.Clamp(value, 0f, Mathf.Min(contentRect.width, contentRect.height) / 2f - thickness / 2f);
			MarkDirtyRepaint();
		}
	}

	public RadialProgress()
	{
		generateVisualContent += context =>
		{
			if (progress <= 0 || thickness <= 0)
				return;

			var rect = contentRect;

			if (radius <= 0f)
				return;

			var painter = context.painter2D;
			painter.strokeColor = color;
			painter.lineWidth = thickness;
			painter.BeginPath();
			painter.Arc(rect.center, radius, -90f, -90f + progress * 360f);
			painter.Stroke();
		};
	}
}
