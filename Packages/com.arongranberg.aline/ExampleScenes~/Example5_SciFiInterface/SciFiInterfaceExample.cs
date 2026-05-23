using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using Drawing;

namespace Drawing.Examples {
	/// <summary>
	/// Example showing various drawing primitives in an animated sci-fi interface style.
	/// Demonstrates rings, arcs, circles, arrows, lines, and other primitives in a cohesive visual design.
	/// </summary>
	[HelpURL("http://arongranberg.com/aline/documentation/stable/scifiinterfaceexample.html")]
	public class SciFiInterfaceExample : MonoBehaviourGizmos {
		[Header("Colors")]
		/// <summary>Bright red-orange</summary>
		public Color primaryColor = new Color(1.0f, 0.3f, 0.1f, 1.0f);
		/// <summary>Orange</summary>
		public Color secondaryColor = new Color(1.0f, 0.6f, 0.2f, 1.0f);
		/// <summary>Deep red</summary>
		public Color accentColor = new Color(1.0f, 0.15f, 0.05f, 0.8f);
		/// <summary>Soft orange glow</summary>
		public Color glowColor = new Color(1.0f, 0.5f, 0.2f, 0.3f);

		[Header("Animation")]
		/// <summary>Degrees per second</summary>
		public float rotationSpeed = 30f;
		public float pulseSpeed = 2f;
		public float scanSpeed = 1.5f;

		[Header("Layout")]
		public float centerRadius = 2f;
		public float cornerDistance = 4f;

		[Header("Status Indicator Wave")]
		public float waveSpeed = 0.8f;
		/// <summary>Higher = narrower peak</summary>
		public float wavePower = 4f;
		/// <summary>0-1, how much white at peak</summary>
		public float whiteHotspotIntensity = 0.8f;
		/// <summary>Additional radius for outer glow</summary>
		public float outerGlowSize = 0.08f;
		public float outerGlowAlpha = 0.4f;

		float animationTime => Time.time;

		public override void DrawGizmos () {
			// Use 2D drawing context (XZ plane)
			using (Draw.InLocalSpace(transform)) {
				DrawCentralHub();
				DrawScanningRings();
				DrawCornerElements();
				DrawDataStreams();
				DrawStatusIndicators();
			}
		}

		void DrawCentralHub () {
			var center = float3.zero;
			var rotation = animationTime * rotationSpeed;

			// Outer rotating ring system
			for (int i = 0; i < 3; i++) {
				float radius = centerRadius + i * 0.3f;
				float offset = i * 120f; // Phase offset for each ring
				float angle = rotation + offset;

				// Calculate arc parameters
				float arcStart = angle * Mathf.Deg2Rad;
				float arcLength = Mathf.Lerp(180f, 270f, (Mathf.Sin(animationTime * pulseSpeed + i) + 1) * 0.5f);
				float arcEnd = arcStart + arcLength * Mathf.Deg2Rad;

				// Draw animated arc segment
				var quat = Quaternion.Euler(0, angle, 0);
				var color = Color.Lerp(primaryColor, secondaryColor, i / 3f);
				Draw.xz.SolidRing(center, radius - 0.08f, radius + 0.08f, arcStart, arcEnd, color);

				// Add glow effect
				Draw.xz.SolidRing(center, radius - 0.12f, radius + 0.12f, arcStart, arcEnd,
					new Color(glowColor.r, glowColor.g, glowColor.b, glowColor.a * 0.5f));
			}

			// Inner pulsing core
			float corePulse = (Mathf.Sin(animationTime * pulseSpeed * 2) + 1) * 0.5f;
			float coreRadius = centerRadius * 0.4f * (0.8f + corePulse * 0.2f);
			Draw.xz.SolidCircle(center, coreRadius, primaryColor);
			Draw.xz.SolidCircle(center, coreRadius * 1.15f, glowColor);

			// Static structural rings
			Draw.xz.Circle(center, centerRadius * 0.6f, accentColor);
			Draw.xz.Circle(center, centerRadius * 1.3f, accentColor);

			// Crosshair indicators
			float crosshairLength = centerRadius * 0.5f;
			for (int i = 0; i < 4; i++) {
				float angle = i * 90f;
				var dir = new float3(Mathf.Cos(angle * Mathf.Deg2Rad), 0, Mathf.Sin(angle * Mathf.Deg2Rad));
				var start = center + dir * centerRadius * 0.55f;
				var end = center + dir * centerRadius * 0.65f;
				Draw.Line(start, end, secondaryColor);
			}
		}

		void DrawScanningRings () {
			var center = float3.zero;

			// Expanding scan rings
			for (int i = 0; i < 3; i++) {
				float timeOffset = i * 0.5f;
				float scanTime = (animationTime * scanSpeed + timeOffset) % 3f;
				float t = scanTime / 3f;

				float radius = Mathf.Lerp(centerRadius * 1.4f, centerRadius * 2.5f, t);
				float alpha = (1 - t) * 0.6f;

				var color = new Color(secondaryColor.r, secondaryColor.g, secondaryColor.b, alpha);
				Draw.xz.Circle(center, radius, color);

				// Add dots on the ring
				int numDots = 24;
				for (int d = 0; d < numDots; d++) {
					float angle = (d / (float)numDots) * Mathf.PI * 2;
					var pos = center + new float3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
					Draw.xz.SolidCircle(pos, 0.03f, color);
				}
			}
		}

		void DrawCornerElements () {
			// Draw decorative elements in four corners
			for (int corner = 0; corner < 4; corner++) {
				float angle = corner * 90f + 45f; // Diagonal corners
				var direction = new float3(Mathf.Cos(angle * Mathf.Deg2Rad), 0, Mathf.Sin(angle * Mathf.Deg2Rad));
				var cornerPos = direction * cornerDistance;

				DrawCornerWidget(cornerPos, angle, corner);
			}
		}

		void DrawCornerWidget (float3 position, float angle, int index) {
			float time = animationTime + index * 0.5f;

			// Rotating bracket
			float bracketRotation = time * rotationSpeed * 0.5f;
			var quat = Quaternion.Euler(0, bracketRotation, 0);

			// Different widget for each corner showcasing different primitives
			switch (index % 4) {
			case 0:     // Ring with arc
				Draw.xz.WireRing(position, 0.3f, 0.4f, accentColor);
				float arcPulse = (Mathf.Sin(time * pulseSpeed) + 1) * 0.5f;
				float arcStart = 0;
				float arcEnd = Mathf.Lerp(Mathf.PI * 0.5f, Mathf.PI * 1.5f, arcPulse);
				Draw.xz.SolidRing(position, 0.32f, 0.38f, arcStart, arcEnd, primaryColor);
				break;

			case 1:     // Nested circles
				float pulse = (Mathf.Sin(time * pulseSpeed * 1.2f) + 1) * 0.5f;
				Draw.xz.Circle(position, 0.4f, accentColor);
				Draw.xz.SolidCircle(position, 0.3f * (0.7f + pulse * 0.3f), secondaryColor);
				break;

			case 2:     // Box with rotation - tilted to show 3D in top-down view
				var tiltedQuat = Quaternion.Euler(45f, bracketRotation, 30f);
				Draw.WireBox(position, tiltedQuat, new float3(0.5f, 0.5f, 0.5f), primaryColor);
				Draw.xz.Circle(position, 0.25f, accentColor);
				break;

			case 3:     // Arrows
				using (Draw.WithColor(secondaryColor)) {
					for (int i = 0; i < 3; i++) {
						float a = i * 120f * Mathf.Deg2Rad + time * 2;
						var dir = new float3(Mathf.Cos(a), 0, Mathf.Sin(a));
						var arrowStart = position + dir * 0.15f;
						var arrowEnd = position + dir * 0.4f;
						Draw.Arrow(arrowStart, arrowEnd, new float3(0, 1, 0), 0.08f);
					}
				}
				break;
			}

			// Corner brackets
			float bracketSize = 0.15f;
			for (int i = 0; i < 4; i++) {
				float a = i * 90f + 45f + angle;
				var dir = new float3(Mathf.Cos(a * Mathf.Deg2Rad), 0, Mathf.Sin(a * Mathf.Deg2Rad));
				var bracketPos = position + dir * 0.5f;

				// L-shaped bracket
				var perpDir = new float3(-dir.z, 0, dir.x);
				Draw.Line(bracketPos, bracketPos + dir * bracketSize, accentColor);
				Draw.Line(bracketPos, bracketPos + perpDir * bracketSize * 0.5f, accentColor);
			}
		}

		void DrawDataStreams () {
			// Animated lines flowing from center to corners
			float streamTime = animationTime * 2f;

			for (int i = 0; i < 8; i++) {
				float angle = i * 45f;
				var dir = new float3(Mathf.Cos(angle * Mathf.Deg2Rad), 0, Mathf.Sin(angle * Mathf.Deg2Rad));

				// Multiple dots traveling along the line
				for (int d = 0; d < 3; d++) {
					float dotTime = (streamTime + i * 0.3f + d * 0.5f) % 2f;
					float t = dotTime / 2f;

					float startDist = centerRadius * 1.5f;
					float endDist = cornerDistance * 0.9f;
					float dist = Mathf.Lerp(startDist, endDist, t);

					var dotPos = dir * dist;
					float alpha = Mathf.Sin(t * Mathf.PI) * 0.8f;
					var color = new Color(secondaryColor.r, secondaryColor.g, secondaryColor.b, alpha);

					Draw.xz.SolidCircle(dotPos, 0.04f, color);
				}
			}
		}

		void DrawStatusIndicators () {
			// Small status indicators showing various primitive types
			float indicatorDistance = centerRadius * 2.2f;
			int numIndicators = 12;

			for (int i = 0; i < numIndicators; i++) {
				float angle = (i / (float)numIndicators) * Mathf.PI * 2;
				var pos = new float3(Mathf.Cos(angle) * indicatorDistance, 0, Mathf.Sin(angle) * indicatorDistance);

				// Smooth traveling wave pattern with sharp falloff
				// Wave travels around the circle over time
				float wavePhase = animationTime * waveSpeed;
				float dotAngle = (i / (float)numIndicators) * Mathf.PI * 2;

				// Calculate brightness using a cosine wave with exponential falloff
				// This creates a narrow bright peak that travels around
				float phase = dotAngle - wavePhase;
				float brightness = Mathf.Cos(phase);
				brightness = Mathf.Max(0, brightness); // Clip negative values
				brightness = Mathf.Pow(brightness, wavePower); // Sharp falloff (higher power = narrower peak)

				// Interpolate between dim and bright states
				var dotColor = Color.Lerp(
					new Color(accentColor.r, accentColor.g, accentColor.b, 0.3f), // Dim state
					primaryColor, // Bright state
					brightness
					);

				// At peak brightness, add white hotspot
				if (brightness > 0.5f) {
					float whiteness = (brightness - 0.5f) * 2f; // 0 to 1 as brightness goes from 0.5 to 1
					dotColor = Color.Lerp(dotColor, Color.white, whiteness * whiteHotspotIntensity);
				}

				var glowAlpha = brightness * glowColor.a;

				// Draw the indicator with multiple glow layers
				Draw.xz.SolidCircle(pos, 0.06f, dotColor);
				if (brightness > 0.1f) {
					// Inner glow
					Draw.xz.SolidCircle(pos, 0.09f, new Color(glowColor.r, glowColor.g, glowColor.b, glowAlpha));

					// Outer glow - larger and more transparent, scales with brightness
					float outerGlowRadius = 0.12f + brightness * outerGlowSize;
					float glowAlphaScaled = brightness * outerGlowAlpha;
					Draw.xz.SolidCircle(pos, outerGlowRadius, new Color(1f, 0.6f, 0.3f, glowAlphaScaled));
				}

				// Connecting line to center ring
				var centerEdge = float3.zero + math.normalize(pos) * centerRadius * 1.35f;
				Draw.DashedLine(centerEdge, pos, 0.05f, 0.05f, new Color(accentColor.r, accentColor.g, accentColor.b, 0.3f));
			}
		}
	}
}
