using System;
using System.Runtime.Serialization;
using System.Collections.Generic;
using UnityEngine;
using ColossalFramework;
using static ColossalFramework.Math.VectorUtils;
using TrafficManager.API.Traffic.Enums;
using KianCommons;
using static KianCommons.ReflectionHelpers;
using KianCommons.Serialization;

using System.Diagnostics;
using System.Linq;
using ModsCommon.Utilities;
using KianCommons.Math;
using ModsCommon;
using ModsCommon.UI;
using ColossalFramework.UI;

namespace NodeController
{
    [Serializable]
    public class NodeData : ISerializable, INetworkData, INetworkData<NodeData>
    {
        private static SegmentComparer SegmentComparer { get; } = new SegmentComparer();
        #region PROPERTIES

        public string Title => $"Node #{NodeId}";

        public ushort NodeId { get; set; }
        public NetNode Node => NodeId.GetNode();
        public NetInfo Info => Node.Info;
        private NodeStyle Style { get; set; }
        public NodeStyleType Type
        {
            get => Style.Type;
            set
            {
                NodeStyle newType = value switch
                {
                    NodeStyleType.Middle => new MiddleNode(this),
                    NodeStyleType.Bend => new BendNode(this),
                    NodeStyleType.Stretch => new StretchNode(this),
                    NodeStyleType.Crossing => new CrossingNode(this),
                    NodeStyleType.UTurn => new UTurnNode(this),
                    NodeStyleType.Custom => new CustomNode(this),
                    NodeStyleType.End => new EndNode(this),
                    _ => throw new NotImplementedException(),
                };
                Style = newType;
                Refresh();
            }
        }
        public IEnumerable<SegmentEndData> SegmentEndDatas => Node.SegmentIds().Select(s => SegmentEndManager.Instance[s, NodeId]);
        public bool IsDefault => Type == DefaultType && !SegmentEndDatas.Any(s => s?.IsDefault != true);

        public NetNode.Flags DefaultFlags { get; set; }
        public NodeStyleType DefaultType { get; set; }

        private List<ushort> SegmentIdsList { get; set; }
        public bool IsEnd => SegmentIdsList.Count == 1;
        public bool IsMain => SegmentIdsList.Count == 2;
        public bool IsJunction => SegmentIdsList.Count > 2;
        public IEnumerable<ushort> SegmentIds => SegmentIdsList;
        public int SegmentCount => SegmentIdsList.Count;

        ushort FirstSegmentId => IsMain ? SegmentIdsList[0] : 0;
        ushort SecondSegmentId => IsMain ? SegmentIdsList[1] : 0;
        NetSegment FirstSegment => FirstSegmentId.GetSegment();
        NetSegment SecondSegment => SecondSegmentId.GetSegment();
        public SegmentEndData FirstSegmentEnd => SegmentEndManager.Instance[FirstSegmentId, NodeId];
        public SegmentEndData SecondSegmentEnd => SegmentEndManager.Instance[SecondSegmentId, NodeId];

        public bool HasPedestrianLanes => SegmentIdsList.Any(s => s.GetSegment().Info.m_hasPedestrianLanes);
        private int PedestrianLaneCount => SegmentIdsList.Max(s => s.GetSegment().Info.CountPedestrianLanes());
        private float MainDot => DotXZ(FirstSegment.GetDirection(NodeId).XZ(), SecondSegment.GetDirection(NodeId).XZ());
        public bool IsStraight => IsMain && MainDot < -0.99f;
        public bool Is180 => IsMain && MainDot > 0.99f;
        public bool IsEqualWidth => IsMain && Math.Abs(FirstSegment.Info.m_halfWidth - SecondSegment.Info.m_halfWidth) < 0.001f;

        public bool FirstTimeTrafficLight { get; set; }

        public bool IsFlatJunctions
        {
            get => SegmentIdsList.Take(2).All(s => SegmentEndManager.Instance[s, NodeId, true].IsFlat);
            set
            {
                var count = 0;
                foreach (ushort segmentId in SegmentIdsList)
                {
                    var segmentEnd = SegmentEndManager.Instance[segmentId, NodeId, true];
                    if (value)
                    {
                        segmentEnd.IsFlat = true;
                        segmentEnd.Twist = false;
                    }
                    else
                    {
                        segmentEnd.IsFlat = count >= 2;
                        segmentEnd.Twist = count >= 2;
                    }
                    count += 1;
                }
                Update();
            }
        }
        public float Offset
        {
            get => SegmentIdsList.Average(s => SegmentEndManager.Instance[s, NodeId, true].Offset);
            set
            {
                foreach (var segmentId in SegmentIdsList)
                {
                    var segmentEnd = SegmentEndManager.Instance[segmentId, NodeId, true];
                    segmentEnd.Offset = value;
                }
                Update();
            }
        }
        public float Shift
        {
            get => SegmentIdsList.Average(s => SegmentEndManager.Instance[s, NodeId, true].Shift);
            set
            {
                foreach (var segmentId in SegmentIdsList)
                {
                    var segmentEnd = SegmentEndManager.Instance[segmentId, NodeId, true];
                    segmentEnd.Shift = value;
                }
                Update();
            }
        }
        public float RotateAngle
        {
            get => SegmentIdsList.Average(s => SegmentEndManager.Instance[s, NodeId, true].RotateAngle);
            set
            {
                foreach (var segmentId in SegmentIdsList)
                {
                    var segmentEnd = SegmentEndManager.Instance[segmentId, NodeId, true];
                    segmentEnd.RotateAngle = value;
                }
                Update();
            }
        }
        public float SlopeAngle
        {
            get => IsMain ? (FirstSegmentEnd.SlopeAngle - SecondSegmentEnd.SlopeAngle) / 2 : 0f;
            set
            {
                if (IsMain)
                {
                    FirstSegmentEnd.SlopeAngle = value;
                    SecondSegmentEnd.SlopeAngle = -value;
                    Update();
                }
            }
        }
        public float TwistAngle
        {
            get => IsMain ? (FirstSegmentEnd.TwistAngle - SecondSegmentEnd.TwistAngle) / 2 : 0f;
            set
            {
                if (IsMain)
                {
                    FirstSegmentEnd.TwistAngle = value;
                    SecondSegmentEnd.TwistAngle = -value;
                    Update();
                }
            }
        }

        public bool NoMarkings
        {
            get => Node.SegmentIds().Any(s => SegmentEndManager.Instance[s, NodeId, true].NoMarkings);
            set
            {
                foreach (var segmentId in Node.SegmentIds())
                {
                    var segmentEnd = SegmentEndManager.Instance[segmentId, NodeId, true];
                    segmentEnd.NoMarkings = value;
                }
                Update();
            }
        }
        public float Stretch
        {
            get => (FirstSegmentEnd.Stretch + SecondSegmentEnd.Stretch) / 2;
            set
            {
                FirstSegmentEnd.Stretch = value;
                SecondSegmentEnd.Stretch = value;
                Update();
            }
        }

        public bool IsCSUR => NetUtil.IsCSUR(Info);
        public bool IsRoad => Info.m_netAI is RoadBaseAI;

        public bool IsEndNode => Type == NodeStyleType.End;
        public bool IsMiddleNode => Type == NodeStyleType.Middle;
        public bool IsBendNode => Type == NodeStyleType.Bend;
        public bool IsJunctionNode => !IsMiddleNode && !IsBendNode && !IsEndNode;
        public bool IsMoveableNode => IsMiddleNode && Style.IsDefault;


        public bool WantsTrafficLight => Type == NodeStyleType.Crossing;
        public bool CanModifyOffset => Type == NodeStyleType.Bend || Type == NodeStyleType.Stretch || Type == NodeStyleType.Custom;
        public bool CanMassEditNodeCorners => IsMain;
        public bool CanModifyFlatJunctions => !IsMiddleNode;
        public bool IsAsymRevert => DefaultFlags.IsFlagSet(NetNode.Flags.AsymBackward | NetNode.Flags.AsymForward);
        public bool CanModifyTextures => IsRoad && !IsCSUR;
        public bool ShowNoMarkingsToggle => CanModifyTextures && Type == NodeStyleType.Custom;
        public bool NeedsTransitionFlag => IsMain && (Type == NodeStyleType.Custom || Type == NodeStyleType.Crossing || Type == NodeStyleType.UTurn);
        public bool ShouldRenderCenteralCrossingTexture => Type == NodeStyleType.Crossing && CrossingIsRemoved(FirstSegmentId) && CrossingIsRemoved(SecondSegmentId);


        public bool? IsUturnAllowedConfigurable => Type switch
        {
            NodeStyleType.Crossing or NodeStyleType.Stretch or NodeStyleType.Middle or NodeStyleType.Bend => false,// always off
            NodeStyleType.UTurn or NodeStyleType.Custom or NodeStyleType.End => null,// default
            _ => throw new Exception("Unreachable code"),
        };
        public bool? IsDefaultUturnAllowed => Type switch
        {
            NodeStyleType.UTurn => true,
            NodeStyleType.Crossing or NodeStyleType.Stretch => false,
            NodeStyleType.Middle or NodeStyleType.Bend or NodeStyleType.Custom or NodeStyleType.End => null,
            _ => throw new Exception("Unreachable code"),
        };
        public bool? IsPedestrianCrossingAllowedConfigurable => Type switch
        {
            NodeStyleType.Crossing or NodeStyleType.UTurn or NodeStyleType.Stretch or NodeStyleType.Middle or NodeStyleType.Bend => false,
            NodeStyleType.Custom => (IsMain && !HasPedestrianLanes) ? false : null,
            NodeStyleType.End => null,
            _ => throw new Exception("Unreachable code"),
        };
        public bool? IsDefaultPedestrianCrossingAllowed => Type switch
        {
            NodeStyleType.Crossing => true,
            NodeStyleType.UTurn or NodeStyleType.Stretch or NodeStyleType.Middle or NodeStyleType.Bend => false,
            NodeStyleType.Custom when IsMain && FirstSegment.Info.m_netAI.GetType() != SecondSegment.Info.m_netAI.GetType() => false,
            NodeStyleType.Custom or NodeStyleType.End => null,
            _ => throw new Exception("Unreachable code"),
        };
        public bool? CanHaveTrafficLights(out ToggleTrafficLightError reason)
        {
            reason = ToggleTrafficLightError.None;
            switch (Type)
            {
                case NodeStyleType.Crossing:
                case NodeStyleType.UTurn:
                case NodeStyleType.End:
                case NodeStyleType.Custom:
                    return null;
                case NodeStyleType.Stretch:
                case NodeStyleType.Middle:
                case NodeStyleType.Bend:
                    reason = ToggleTrafficLightError.NoJunction;
                    return false;
                default:
                    throw new Exception("Unreachable code");
            }
        }
        public bool? IsEnteringBlockedJunctionAllowedConfigurable => Type switch
        {
            NodeStyleType.Custom when IsJunction => null,
            NodeStyleType.Custom when DefaultFlags.IsFlagSet(NetNode.Flags.OneWayIn) & DefaultFlags.IsFlagSet(NetNode.Flags.OneWayOut) && !HasPedestrianLanes => false,//
            NodeStyleType.Crossing or NodeStyleType.UTurn or NodeStyleType.Custom or NodeStyleType.End => null,// default off
            NodeStyleType.Stretch or NodeStyleType.Middle or NodeStyleType.Bend => false,// always on
            _ => throw new Exception("Unreachable code"),
        };
        public bool? IsDefaultEnteringBlockedJunctionAllowed => Type switch
        {
            NodeStyleType.Stretch => true,// always on
            NodeStyleType.Crossing => false,// default off
            NodeStyleType.UTurn or NodeStyleType.Middle or NodeStyleType.Bend or NodeStyleType.End => null,// default
            NodeStyleType.Custom => IsJunction ? null : true,
            _ => throw new Exception("Unreachable code"),
        };

        #endregion

        #region BASIC

        public NodeData() { }
        public NodeData(ushort nodeId)
        {
            NodeId = nodeId;
            Calculate();
            Type = DefaultType;
            FirstTimeTrafficLight = false;
            Update();
        }

        public NodeData(ushort nodeId, NodeStyleType nodeType) : this(nodeId)
        {
            Type = nodeType;
            FirstTimeTrafficLight = nodeType == NodeStyleType.Crossing;
        }
        private NodeData(NodeData template) => CopyProperties(this, template);
        public NodeData(SerializationInfo info, StreamingContext context)
        {
            SerializationUtil.SetObjectFields(info, this);

            if (NodeManager.TargetNodeId != 0)
                NodeId = NodeManager.TargetNodeId;

            SerializationUtil.SetObjectProperties(info, this);
            Update();
        }
        public NodeData Clone() => new NodeData(this);
        public void GetObjectData(SerializationInfo info, StreamingContext context) => SerializationUtil.GetObjectFields(info, this);

        public void Calculate()
        {
            var node = Node;
            DefaultFlags = node.m_flags;

            if (DefaultFlags.IsFlagSet(NetNode.Flags.Middle))
                DefaultType = NodeStyleType.Middle;
            else if (DefaultFlags.IsFlagSet(NetNode.Flags.Bend))
                DefaultType = NodeStyleType.Bend;
            else if (DefaultFlags.IsFlagSet(NetNode.Flags.Junction))
                DefaultType = NodeStyleType.Custom;
            else if (DefaultFlags.IsFlagSet(NetNode.Flags.End))
                DefaultType = NodeStyleType.End;
            else
                throw new NotImplementedException($"Unsupported node flags: {DefaultFlags}");

            SegmentIdsList = node.SegmentIds().ToList();
            SegmentIdsList.Sort(SegmentComparer);
            SegmentIdsList.Reverse();

            if (Style == null || !IsPossibleType(Type))
                Type = DefaultType;
        }

        public void Update() => NetManager.instance.UpdateNode(NodeId);
        public void ResetToDefault()
        {
            if (Style.ResetOffset)
                Offset = Style.DefaultOffset;
            if (Style.ResetShift)
                Shift = Style.DefaultShift;
            if (Style.ResetRotate)
                RotateAngle = Style.DefaultRotate;
            if (Style.ResetSlope)
                SlopeAngle = Style.DefaultSlope;
            if (Style.ResetTwist)
                TwistAngle = Style.DefaultTwist;
            if (Style.ResetNoMarking)
                NoMarkings = Style.DefaultNoMarking;
            if (Style.ResetFlatJunction)
                IsFlatJunctions = Style.DefaultFlatJunction;

            foreach (var segmentEnd in SegmentEndDatas)
                segmentEnd.ResetToDefault();
        }
        public void Refresh()
        {
            ResetToDefault();
            Update();
        }

        #endregion

        #region UTILITIES

        public bool IsPossibleType(NodeStyleType newNodeType)
        {
            if (IsJunction || IsCSUR)
                return newNodeType == NodeStyleType.Custom;

            bool middle = DefaultFlags.IsFlagSet(NetNode.Flags.Middle);
            return newNodeType switch
            {
                NodeStyleType.Crossing => IsEqualWidth && IsStraight && PedestrianLaneCount >= 2,
                NodeStyleType.UTurn => IsMain && IsRoad && Info.m_forwardVehicleLaneCount > 0 && Info.m_backwardVehicleLaneCount > 0,
                NodeStyleType.Stretch => CanModifyTextures && !middle && IsStraight,
                NodeStyleType.Bend => !middle,
                NodeStyleType.Middle => IsStraight || Is180,
                NodeStyleType.Custom => true,
                NodeStyleType.End => IsEnd,
                _ => throw new Exception("Unreachable code"),
            };
        }
        public static bool IsSupported(ushort nodeId)
        {
            var node = nodeId.GetNode();
            if (!node.IsValid())
                return false;

            var segmentIds = node.SegmentIds().ToArray();
            if (segmentIds.Any(id => !id.GetSegment().IsValid()))
                return false;

            if (!node.m_flags.CheckFlags(required: NetNode.Flags.Created, forbidden: NetNode.Flags.LevelCrossing | NetNode.Flags.Outside | NetNode.Flags.Deleted))
                return false;

            if (segmentIds.Length != 2)
                return true;

            return !NetUtil.IsCSUR(node.Info);
        }
        bool CrossingIsRemoved(ushort segmentId) => HideCrosswalks.Patches.CalculateMaterialCommons.ShouldHideCrossing(NodeId, segmentId);

        public override string ToString() => $"NodeData(id:{NodeId} type:{Type})";

        #endregion

        #region UI COMPONENTS

        public void GetUIComponents(UIComponent parent, Action refresh)
        {
            GetNodeTypeProperty(parent, refresh);
            Style.GetUIComponents(parent, refresh);
        }

        private NodeTypePropertyPanel GetNodeTypeProperty(UIComponent parent, Action refresh)
        {
            var typeProperty = ComponentPool.Get<NodeTypePropertyPanel>(parent);
            typeProperty.Text = "Node type";
            typeProperty.Init(IsPossibleType);
            typeProperty.SelectedObject = Type;
            typeProperty.OnSelectObjectChanged += (value) =>
            {
                Type = value;
                refresh();
            };

            return typeProperty;
        }

        //public string ToolTip(NodeTypeT nodeType) => nodeType switch
        //{
        //    NodeTypeT.Crossing => "Crossing node.",
        //    NodeTypeT.Middle => "Middle: No node.",
        //    NodeTypeT.Bend => IsAsymRevert ? "Bend: Asymmetrical road changes direction." : (HalfWidthDelta > 0.05f ? "Bend: Linearly match segment widths." : "Bend: Simple road corner."),
        //    NodeTypeT.Stretch => "Stretch: Match both pavement and road.",
        //    NodeTypeT.UTurn => "U-Turn: node with enough space for U-Turn.",
        //    NodeTypeT.Custom => "Custom: transition size and traffic rules are configrable.",
        //    NodeTypeT.End => "when there is only one segment at the node.",
        //    _ => null,
        //};

        #endregion
    }

    public class SegmentComparer : IComparer<ushort>
    {
        public int Compare(ushort firstSegmentId, ushort secondSegmentId)
        {
            var firstInfo = firstSegmentId.GetSegment().Info;
            var secondInfo = secondSegmentId.GetSegment().Info;

            int result;

            if ((result = firstInfo.m_flatJunctions.CompareTo(secondInfo.m_flatJunctions)) == 0)
                if ((result = firstInfo.m_forwardVehicleLaneCount.CompareTo(secondInfo.m_forwardVehicleLaneCount)) == 0)
                    if ((result = firstInfo.m_halfWidth.CompareTo(secondInfo.m_halfWidth)) == 0)
                        result = ((firstInfo.m_netAI as RoadBaseAI)?.m_highwayRules ?? false).CompareTo((secondInfo.m_netAI as RoadBaseAI)?.m_highwayRules ?? false);

            return result;
        }
    }
    public class NodeTypePropertyPanel : EnumOncePropertyPanel<NodeStyleType, NodeTypePropertyPanel.NodeTypeDropDown>
    {
        protected override float DropDownWidth => 100f;
        protected override bool IsEqual(NodeStyleType first, NodeStyleType second) => first == second;
        public class NodeTypeDropDown : UIDropDown<NodeStyleType> { }
        protected override string GetDescription(NodeStyleType value) => value.ToString();
    }
}
