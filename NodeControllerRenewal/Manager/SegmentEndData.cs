using ColossalFramework;
using ColossalFramework.Math;
using ColossalFramework.UI;
using KianCommons;
using System;
using System.Runtime.Serialization;
using UnityEngine;
using System.Linq;
using System.Collections.Generic;
using ModsCommon.Utilities;
using NodeController.Utilities;

namespace NodeController
{
    [Serializable]
    public class SegmentEndData : INetworkData, IOverlay
    {
        #region STATIC

        public static float CircleRadius => 2.5f;
        public static float DotRadius => 0.75f;

        #endregion

        #region PROPERTIES

        public string Title => $"Segment #{Id}";

        public ushort NodeId { get; set; }
        public ushort Id { get; set; }

        public NetSegment Segment => Id.GetSegment();
        public NetInfo Info => Segment.Info;
        public NetNode Node => NodeId.GetNode();
        public NodeData NodeData => Manager.Instance[NodeId];
        public bool IsStartNode => Segment.IsStartNode(NodeId);
        public SegmentEndData Other => Manager.Instance[Segment.GetOtherNode(NodeId), Id, true];
        public BezierTrajectory SegmentBezier { get; private set; }

        public float DefaultOffset => CSURUtilities.GetMinCornerOffset(Id, NodeId);
        public bool DefaultIsFlat => Info.m_flatJunctions || Node.m_flags.IsFlagSet(NetNode.Flags.Untouchable);
        public bool DefaultIsTwist => DefaultIsFlat && !Node.m_flags.IsFlagSet(NetNode.Flags.Untouchable);
        public NetSegment.Flags DefaultFlags { get; set; }

        public int PedestrianLaneCount { get; set; }
        public float CachedSuperElevationDeg { get; set; }

        public bool NoCrossings { get; set; }
        public bool NoMarkings { get; set; }
        public bool NoJunctionTexture { get; set; }
        public bool NoJunctionProps { get; set; }
        public bool NoTLProps { get; set; }
        public bool IsSlope { get; set; }
        public bool IsTwist { get; set; }

        public bool IsDefault
        {
            get
            {
                var ret = SlopeAngle == 0f;
                ret &= TwistAngle == 0;
                ret &= IsSlope == !DefaultIsFlat;
                ret &= IsTwist == DefaultIsTwist;

                ret &= NoCrossings == false;
                ret &= NoMarkings == false;
                ret &= NoJunctionTexture == false;
                ret &= NoJunctionProps == false;
                ret &= NoTLProps == false;
                return ret;
            }
        }
        public float Offset { get; set; }
        public float Shift { get; set; }
        public float RotateAngle { get; set; }
        public float SlopeAngle { get; set; }
        public float TwistAngle { get; set; }

        public bool CanModifyTwist => CanTwist(Id, NodeId);
        public bool? ShouldHideCrossingTexture
        {
            get
            {
                if (NodeData != null && NodeData.Type == NodeStyleType.Stretch)
                    return false; // always ignore.
                else if (NoMarkings)
                    return true; // always hide
                else
                    return null; // default.
            }
        }

        public SegmentCorner this[bool isLeft]
        {
            get => isLeft ? LeftCorner : RightCorner;
            set
            {
                if (isLeft)
                    LeftCorner = value;
                else
                    RightCorner = value;
            }
        }

        private SegmentCorner LeftCorner { get; set; }
        private SegmentCorner RightCorner { get; set; }
        public Vector3 Position { get; private set; }
        public Vector3 Direction => (RightCorner.Direction + LeftCorner.Direction).normalized;
        public Vector3 EndDirection => (RightCorner.Position - LeftCorner.Position).normalized;


        #endregion

        #region BASIC

        public SegmentEndData(ushort segmentId, ushort nodeId)
        {
            Id = segmentId;
            NodeId = nodeId;

            DefaultFlags = Segment.m_flags;
            PedestrianLaneCount = Info.CountPedestrianLanes();

            ResetToDefault();
        }
        public void UpdateNode() => Manager.Instance.Update(NodeId);

        public void ResetToDefault()
        {
            Offset = DefaultOffset;

            IsSlope = !DefaultIsFlat;
            IsTwist = DefaultIsTwist;
            NoCrossings = false;
            NoJunctionTexture = false;
            NoJunctionProps = false;
            NoTLProps = false;
        }

        public void AfterSegmentCalculate()
        {
            CalculatePosition();
            UpdateCachedSuperElevation();
        }
        private void CalculatePosition()
        {
            var line = new StraightTrajectory(LeftCorner.Position, RightCorner.Position);
            var intersect = Intersection.CalculateSingle(line, SegmentBezier);
            Position = line.Position(intersect.IsIntersect ? intersect.FirstT : 0.5f);
        }
        private void UpdateCachedSuperElevation()
        {
            var diff = RightCorner.Position - LeftCorner.Position;
            var se = Mathf.Atan2(diff.y, VectorUtils.LengthXZ(diff));
            CachedSuperElevationDeg = se * Mathf.Rad2Deg;
        }

        public static void UpdateSegmentBezier(ushort segmentId)
        {
            var segment = segmentId.GetSegment();

            var startPos = segment.m_startNode.GetNode().m_position;
            var startDir = segment.m_startDirection;
            var endPos = segment.m_endNode.GetNode().m_position;
            var endDir = segment.m_endDirection;
            ShiftSegment(true, segmentId, ref startPos, ref startDir, ref endPos, ref endDir);

            var bezier = new BezierTrajectory(startPos, startDir, endPos, endDir);

            Manager.GetSegmentData(segmentId, out var start, out var end);
            if (start != null)
                start.SegmentBezier = bezier;
            if (end != null)
                end.SegmentBezier = bezier.Invert();
        }
        public static void ShiftSegment(bool isStart, ushort segmentId, ref Vector3 startPos, ref Vector3 startDir, ref Vector3 endPos, ref Vector3 endDir)
        {
            Manager.GetSegmentData(segmentId, out var start, out var end);
            var startShift = (isStart ? start : end)?.Shift ?? 0f;
            var endShift = (isStart ? end : start)?.Shift ?? 0f;

            if (startShift == 0f && endShift == 0f)
                return;

            var shift = (startShift + endShift) / 2;
            var dir = endPos - startPos;
            var sin = shift / dir.XZ().magnitude;
            var deltaAngle = Mathf.Asin(sin);
            var normal = dir.TurnRad(Mathf.PI / 2 + deltaAngle, true).normalized;

            startPos -= normal * startShift;
            endPos += normal * endShift;
            startDir = startDir.TurnRad(deltaAngle, true);
            endDir = endDir.TurnRad(deltaAngle, true);
        }

        #endregion

        #region UTILITIES

        public static bool CanTwist(ushort segmentId, ushort nodeId)
        {
            var segmentIds = nodeId.GetNode().SegmentIds().ToArray();

            if (segmentIds.Length == 1)
                return false;

            var segment = segmentId.GetSegment();
            var firstSegmentId = segment.GetLeftSegment(nodeId);
            var secondSegmentId = segment.GetRightSegment(nodeId);
            var nodeData = Manager.Instance[nodeId];
            var segmentEnd1 = nodeData[firstSegmentId];
            var segmentEnd2 = nodeData[secondSegmentId];

            bool flat1 = !segmentEnd1?.IsSlope ?? firstSegmentId.GetSegment().Info.m_flatJunctions;
            bool flat2 = !segmentEnd2?.IsSlope ?? secondSegmentId.GetSegment().Info.m_flatJunctions;
            if (flat1 && flat2)
                return false;

            if (segmentIds.Length == 2)
            {
                var dir1 = firstSegmentId.GetSegment().GetDirection(nodeId);
                var dir = segmentId.GetSegment().GetDirection(nodeId);
                if (Mathf.Abs(VectorUtils.DotXZ(dir, dir1)) > 0.999f)
                    return false;
            }

            return true;
        }
        public void Render(OverlayData data) => Render(data, data, data);
        public void Render(OverlayData contourData, OverlayData outterData, OverlayData innerData)
        {
            var data = Manager.Instance[NodeId];

            RenderOther(contourData);
            if (data.IsMoveableEnds)
            {
                RenderCutEnd(contourData);
                RenderOutterCircle(outterData);
                RenderInnerCircle(innerData);
            }
            else
                RenderEnd(contourData);
        }
        private void RenderCutEnd(OverlayData data)
        {
            var leftLine = new StraightTrajectory(LeftCorner.Position, Position);
            leftLine = leftLine.Cut(0f, 1f - (CircleRadius / leftLine.Length));
            leftLine.Render(data);

            var rightLine = new StraightTrajectory(RightCorner.Position, Position);
            rightLine = rightLine.Cut(0f, 1f - (CircleRadius / rightLine.Length));
            rightLine.Render(data);
        }
        private void RenderEnd(OverlayData data) => new StraightTrajectory(LeftCorner.Position, RightCorner.Position).Render(data);
        private void RenderOther(OverlayData data)
        {
            if (Other is SegmentEndData otherSegmentData)
            {
                var otherLeftCorner = otherSegmentData[true];
                var otherRightCorner = otherSegmentData[false];

                var leftSide = new BezierTrajectory(LeftCorner.Position, LeftCorner.Direction, otherRightCorner.Position, otherRightCorner.Direction);
                leftSide.Render(data);
                var rightSide = new BezierTrajectory(RightCorner.Position, RightCorner.Direction, otherLeftCorner.Position, otherLeftCorner.Direction);
                rightSide.Render(data);
                var endSide = new StraightTrajectory(otherLeftCorner.Position, otherRightCorner.Position);
                endSide.Render(data);
            }
        }

        private void RenderInnerCircle(OverlayData data) => RenderCircle(data, DotRadius * 2, 0f);
        private void RenderOutterCircle(OverlayData data) => RenderCircle(data, CircleRadius * 2 + 0.5f, CircleRadius * 2 - 0.5f);

        private void RenderCircle(OverlayData data) => Position.RenderCircle(data);
        private void RenderCircle(OverlayData data, float from, float to)
        {
            data.Width = from;
            do
            {
                RenderCircle(data);
                data.Width = Mathf.Max(data.Width.Value - 0.43f, to);
            }
            while (data.Width > to);
        }

        public override string ToString() => $"{GetType().Name} (segment:{Id} node:{NodeId})";

        #endregion

        #region UI COMPONENTS

        public void GetUIComponents(UIComponent parent, Action refresh)
        {
            throw new NotImplementedException();
        }

        #endregion
    }
    public struct SegmentCorner
    {
        public Vector3 Position;
        public Vector3 Direction;

        public override string ToString() => $"{Position} - {Direction}";
    }
}
