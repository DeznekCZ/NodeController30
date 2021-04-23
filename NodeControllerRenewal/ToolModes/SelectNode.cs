﻿using ColossalFramework.Math;
using ModsCommon;
using ModsCommon.Utilities;
using UnityEngine;

namespace NodeController
{
    public class SelectNodeToolMode : BaseSelectToolMode<Mod, NodeControllerTool>, IToolModePanel, IToolMode<ToolModeType>
    {
        public bool ShowPanel => false;
        public ToolModeType Type => ToolModeType.Select;
        protected override Color32 NodeColor => Colors.Yellow;

        public override string GetToolInfo() => IsHoverNode ? $"Node {HoverNode.Id}" : "Select node";

        protected override bool IsValidNode(ushort nodeId)
        {
            if (!Settings.SnapToggle)
                return true;

            var node = nodeId.GetNode();
            return node.m_flags.CheckFlags(0, NetNode.Flags.Middle) || node.m_flags.CheckFlags(0, NetNode.Flags.Moveable);
        }

        public override void OnPrimaryMouseClicked(Event e)
        {
            if (IsHoverNode)
                Set(SingletonManager<Manager>.Instance[HoverNode.Id, true]);
            else if (IsHoverSegment)
            {
                var controlPoint = new NetTool.ControlPoint() { m_segment = HoverSegment.Id };
                HoverSegment.GetHitPosition(Tool.Ray, out _, out controlPoint.m_position);
                if (PossibleInsertNode(controlPoint.m_position))
                    Set(SingletonManager<Manager>.Instance.InsertNode(controlPoint));
            }
        }
        private void Set(NodeData data)
        {
            if (data != null)
            {
                Tool.SetData(data);
                Tool.SetDefaultMode();
            }
        }
        public bool PossibleInsertNode(Vector3 position)
        {
            if (!IsHoverSegment)
                return false;

            foreach (var data in HoverSegment.Datas)
            {
                var node = data.Id.GetNode();
                if (Settings.SnapToggle && node.m_flags.CheckFlags(NetNode.Flags.Moveable, NetNode.Flags.End))
                    continue;

                var gap = 8f + data.halfWidth * 2f * Mathf.Sqrt(1 - data.DeltaAngleCos * data.DeltaAngleCos);
                if ((data.Position - position).sqrMagnitude < gap * gap)
                    return false;
            }

            return true;
        }

        public override void RenderOverlay(RenderManager.CameraInfo cameraInfo)
        {
            if (IsHoverSegment)
            {
                SegmentEndData.CalculateSegmentBeziers(HoverSegment.Id, out var bezier, out _, out _);
                bezier.Trajectory.GetHitPosition(Tool.Ray, out _, out var t, out var position);
                var direction = bezier.Tangent(t).MakeFlatNormalized();

                var halfWidth =SingletonManager<Manager>.Instance.GetSegmentWidth(HoverSegment.Id, t);

                var overlayData = new OverlayData(cameraInfo) { Width = halfWidth * 2, Color = PossibleInsertNode(position) ? Colors.Green : Colors.Red, AlphaBlend = false, Cut = true };

                var middle = new Bezier3()
                {
                    a = position + direction,
                    b = position,
                    c = position,
                    d = position - direction,
                };
                middle.RenderBezier(overlayData);

                overlayData.Width = Selection.BorderOverlayWidth;
                overlayData.Cut = false;

                var normal = direction.MakeFlatNormalized().Turn90(true);
                RenderBorder(overlayData, position + direction, normal, halfWidth);
                RenderBorder(overlayData, position - direction, normal, halfWidth);
            }
            else
                base.RenderOverlay(cameraInfo);
        }
        private void RenderBorder(OverlayData overlayData, Vector3 position, Vector3 normal, float halfWidth)
        {
            var bezier = new Bezier3
            {
                a = position + normal * (halfWidth - Selection.BorderOverlayWidth / 2),
                b = position,
                c = position,
                d = position - normal * (halfWidth - Selection.BorderOverlayWidth / 2),
            };
            bezier.RenderBezier(overlayData);
        }
    }
}