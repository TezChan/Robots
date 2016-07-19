﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Rhino.Geometry;
using System.Collections;
using static Robots.Util;
using static System.Math;
using static Rhino.RhinoMath;

namespace Robots
{
    public abstract class Target
    {
        [Flags]
        public enum RobotConfigurations { None = 0, Shoulder = 1, Elbow = 2, Wrist = 4 }
        public enum Motions { Joint, Linear, Circular, Spline }

        public Tool Tool { get; set; }
        public Frame Frame { get; set; }
        public Speed Speed { get; set; }
        public Zone Zone { get; set; }
        public Command Command { get; set; }
        public double[] External { get; set; }

        public static Target Default { get; }

        static Target()
        {
            Default = new JointTarget(new double[] { 0, PI / 2, 0, 0, 0, 0 }, Tool.Default, Speed.Default, Zone.Default, null, Frame.Default, null);
        }

        public Target(Tool tool, Speed speed, Zone zone, Command command, Frame frame = null, double[] external = null)
        {
            this.Tool = tool;
            this.Speed = speed;
            this.Zone = zone;
            this.Command = command;
            this.Frame = frame;
            this.External = external;
        }

        public Target ShallowClone() => MemberwiseClone() as Target;
    }

    public class CartesianTarget : Target
    {
        public Plane Plane { get; set; }
        public RobotConfigurations? Configuration { get; set; }
        public Motions Motion { get; set; }

        public CartesianTarget(Plane plane, RobotConfigurations? configuration = null, Motions motion = Motions.Joint, Tool tool = null, Speed speed = null, Zone zone = null, Command command = null, Frame frame = null, double[] external = null) : base(tool, speed, zone, command, frame, external)
        {
            this.Plane = plane;
            this.Motion = motion;
            this.Configuration = configuration;
        }

        public CartesianTarget(Plane plane, Target target, RobotConfigurations? configuration = null, Motions motion = Motions.Joint, double[] external = null) : this(plane, configuration, motion, target.Tool, target.Speed, target.Zone, target.Command, target.Frame, external ?? target.External) { }

        /// <summary>
        /// Quaternion interpolation based on: http://www.grasshopper3d.com/group/lobster/forum/topics/lobster-reloaded
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <param name="t"></param>
        /// <param name="min"></param>
        /// <param name="max"></param>
        /// <returns></returns>
        public static Plane Lerp(Plane a, Plane b, double t, double min, double max)
        {
            t = (t - min) / (max - min);
            if (double.IsNaN(t)) t = 0;
            var newOrigin = a.Origin * (1 - t) + b.Origin * t;

            Quaternion q = Quaternion.Rotation(a, b);
            double angle;
            Vector3d axis;
            q.GetRotation(out angle, out axis);
            angle = (angle > PI) ? angle - 2 * PI : angle;
            a.Rotate(t * angle, axis, a.Origin);

            a.Origin = newOrigin;
            return a;
        }

        public override string ToString()
        {
            string type = $"Cartesian ({Plane.OriginX:0.00},{Plane.OriginY:0.00},{Plane.OriginZ:0.00})";
            string motion = $", {Motion.ToString()}";
            string configuration = Configuration != null ? $", \"{Configuration.ToString()}\"" : "";
            string frame = Frame != null ? $", Frame ({Frame.Plane.OriginX:0.00},{Frame.Plane.OriginY:0.00},{Frame.Plane.OriginZ:0.00})" : "";
            string tool = Tool != null ? $", {Tool}" : "";
            string speed = Speed != null ? $", {Speed}" : "";
            string zone = Zone != null ? $", {Zone}" : "";
            string commands = Command != null ? ", Contains commands" : "";
            string external = External != null ? ", External axis" : "";
            return $"Target ({type}{motion}{configuration}{frame}{tool}{speed}{zone}{commands}{external})";
        }

    }

    public class JointTarget : Target
    {
        public double[] Joints { get; set; }

        public JointTarget(double[] joints, Tool tool = null, Speed speed = null, Zone zone = null, Command command = null, Frame frame = null, double[] external = null) : base(tool, speed, zone, command, frame, external)
        {
            this.Joints = joints;
        }

        public JointTarget(double[] joints, Target target, double[] external = null) : this(joints, target.Tool, target.Speed, target.Zone, target.Command, target.Frame, external ?? target.External) { }

        public static double[] Lerp(double[] a, double[] b, double t, double min, double max)
        {
            t = (t - min) / (max - min);
            if (double.IsNaN(t)) t = 0;
            return a.Zip(b, (x, y) => (x * (1 - t) + y * t)).ToArray();
        }

        public static double GetAbsoluteJoint(double joint)
        {
            double PI2 = PI * 2;
            double absJoint = Abs(joint);
            double result = absJoint - Floor(absJoint / PI2) * PI2;
            if (result > PI) result = (result - PI2);
            result *= Sign(joint);
            return result;
        }

        public static double[] GetAbsoluteJoints(double[] joints, double[] prevJoints)
        {
            double[] closestJoints = new double[joints.Length];

            for (int i = 0; i < joints.Length; i++)
            {
                double PI2 = PI * 2;
                double prevJoint = GetAbsoluteJoint(prevJoints[i]);
                double joint = GetAbsoluteJoint(joints[i]);
                double difference = joint - prevJoint;
                double absDifference = Abs(difference);
                if (absDifference > PI) difference = (absDifference - PI2) * Sign(difference);
                closestJoints[i] = prevJoints[i] + difference;
            }

            return closestJoints;
        }

        public override string ToString()
        {
            string type = $"Joint ({string.Join(",", (this as JointTarget).Joints.Select(x => $"{x:0.00}"))})";
            string tool = Tool != null ? $", {Tool}" : "";
            string speed = Speed != null ? $", {Speed}" : "";
            string zone = Zone != null ? $", {Zone}" : "";
            string commands = Command != null ? ", Contains commands" : "";
            string external = External != null ? ", External axis" : "";
            return $"Target ({type}{tool}{speed}{zone}{commands}{external})";
        }

    }

    public class ProgramTarget : Target
    {
        public Plane Plane { get; internal set; }
        public Plane WorldPlane { get; internal set; }
        public double[] Joints { get; internal set; }
        public bool IsJointTarget { get; internal set; }
        public Motions Motion { get; internal set; }
        public RobotConfigurations? Configuration { get; internal set; }

        public int Index { get; internal set; }
        public int Group { get; internal set; }
        public bool ForcedConfiguration { get; internal set; }
        public bool ChangesConfiguration { get; internal set; } = false;
        public List<string> Warnings { get; internal set; }
        public double Time { get; internal set; }
        public double MinTime { get; internal set; }
        public double TotalTime { get; internal set; }
        public int LeadingJoint { get; internal set; }

        public bool IsJointMotion => IsJointTarget || Motion == Motions.Joint;

        internal ProgramTarget(Target target) : base(target.Tool, target.Speed, target.Zone, null, target.Frame, target.External)
        {
            if (target.Command != null)
            {
                if (target.Command is Commands.Group)
                    this.Command = new Commands.Group((target.Command as Commands.Group).Flatten());
                else
                    this.Command = new Commands.Group(new Command[] { target.Command });
            }

            if (target is CartesianTarget)
            {
                var cartesianTarget = target as CartesianTarget;
                this.Plane = cartesianTarget.Plane;
                this.IsJointTarget = false;
                this.Motion = cartesianTarget.Motion;
                this.Configuration = cartesianTarget.Configuration;
                this.ForcedConfiguration = (this.Configuration != null);
            }
            else if (target is JointTarget)
            {
                this.Joints = (target as JointTarget).Joints;
                this.IsJointTarget = true;
            }
            else if (target is ProgramTarget)
                throw new InvalidCastException("Use the ShallowClone() method for duplicating a ProgramTarget");
        }

        public double[] GetAbsoluteExternal()
        {
            double[] external = null;
            if (Joints != null && Joints.Length > 6)
            {
                int externalCount = Joints.Length - 6;
                external = new double[externalCount];
                for (int i = 0; i < externalCount; i++)
                    external[i] = Joints[i + 6];
            }
            else
                external = External;

            return external;
        }

        public Plane GetPrevPlane(ProgramTarget prevTarget)
        {
            Plane prevPlane = prevTarget.Plane;
            if (prevTarget.Tool != this.Tool)
                prevPlane.Transform(Transform.PlaneToPlane(this.Tool.Tcp, prevTarget.Tool.Tcp));
            if (prevTarget.Frame != this.Frame)
                prevPlane.Transform(Transform.PlaneToPlane(this.Frame.Plane, prevTarget.Frame.Plane));

            return prevPlane;
        }

        public Target ToTarget()
        {
            if (this.IsJointTarget)
            {
                return new JointTarget(this.Joints, this, this.External);
            }
            else
            {
                return new CartesianTarget(this.Plane, this, this.Configuration, this.Motion, this.External);
            }
        }

        public Target Lerp(ProgramTarget prevTarget, double t, double start, double end)
        {
           // double[] external = External;
            //if (External != null && prevTarget != null)
            {
                //  external = JointTarget.Lerp(prevTarget.External, External, t, start, end);
                //     external = JointTarget.Lerp(prevTarget.GetAbsoluteExternal(), GetAbsoluteExternal(), t, start, end);
                // external = external.Select(x => JointTarget.GetAbsoluteJoint(x)).ToArray();
              //  external = GetAbsoluteExternal();
            }

            double[] joints = (prevTarget != null) ? JointTarget.Lerp(prevTarget.Joints, Joints, t, start, end) : this.Joints;

            double[] external = External;
            if (External != null && prevTarget != null)
            {
                int externalCount = Joints.Length - 6;
                external = new double[externalCount];
                for (int i = 0; i < externalCount; i++)
                    external[i] = Joints[i + 6];
            }

            if (IsJointMotion)
            {
                return new JointTarget(joints, this, external);
            }
            else if (Motion == Target.Motions.Linear)
            {
                Plane plane = (prevTarget != null) ? CartesianTarget.Lerp(GetPrevPlane(prevTarget), Plane, t, start, end) : this.Plane;
                Target.RobotConfigurations? configuration = (Abs(prevTarget.TotalTime - t) < TimeTol && prevTarget != null) ? prevTarget.Configuration : Configuration;
                return new CartesianTarget(plane, this, configuration, Target.Motions.Linear, external);
            }
            else
                return null;
        }


        public double[] GetAllJoints()
        {
            if (External == null) return Joints;

            double[] allJoints = new double[External.Length + 6];

            if (Joints != null)
            {
                for (int i = 0; i < 6; i++)
                    allJoints[i] = Joints[i];
            }

            if (External != null)
            {
                for (int i = 0; i < External.Length; i++)
                    allJoints[6 + i] = External[i];
            }

            return allJoints;
        }

        internal void SetTargetKinematics(KinematicSolution kinematics)
        {
            if (this.IsJointTarget)
            {
                this.WorldPlane = kinematics.Planes[kinematics.Planes.Length - 1];
                Plane plane = this.WorldPlane;
                plane.Transform(Transform.PlaneToPlane(this.Frame.Plane, Plane.WorldXY));
                this.Plane = plane;
                // this.Joints = this.GetAllJoints();
                this.Joints = kinematics.Joints;
            }
            else
            {
                var worldPlane = this.Plane;
                worldPlane.Transform(Transform.PlaneToPlane(Plane.WorldXY, this.Frame.Plane));
                this.WorldPlane = worldPlane;
                this.Joints = kinematics.Joints;
            }

            this.Configuration = kinematics.Configuration;
        }
    }

    public abstract class TargetAttribute
    {
        /// <summary>
        /// Name of the attribute
        /// </summary>
        public string Name { get; internal set; }

        internal TargetAttribute SetName(string name)
        {
            var attribute = MemberwiseClone() as TargetAttribute;
            attribute.Name = name;
            return attribute;
        }
    }

    public class Tool : TargetAttribute
    {
        public Plane Tcp { get; set; }
        public double Weight { get; set; }
        public Point3d Centroid { get; set; }
        public Mesh Mesh { get; set; }

        public static Tool Default { get; }

        static Tool()
        {
            Default = new Tool(Plane.WorldXY, "DefaultTool");
        }

        public Tool(Plane tcp, string name = null, double weight = 0, Point3d? centroid = null, Mesh mesh = null)
        {
            this.Name = name;
            this.Tcp = tcp;
            this.Weight = weight;
            this.Centroid = (centroid == null) ? tcp.Origin : (Point3d)centroid;
            this.Mesh = mesh;
        }

        public void FourPointCalibration(Plane a, Plane b, Plane c, Plane d)
        {
            var calibrate = new CircumcentreSolver(a.Origin, b.Origin, c.Origin, d.Origin);
            Point3d tcpOrigin = Point3d.Origin;
            foreach (Plane plane in new Plane[] { a, b, c, d })
            {
                Point3d remappedPoint;
                plane.RemapToPlaneSpace(calibrate.Center, out remappedPoint);
                tcpOrigin += remappedPoint;
            }
            tcpOrigin /= 4;
            Tcp = new Plane(tcpOrigin, Tcp.XAxis, Tcp.YAxis);
        }

        /// <summary>
        /// Code lifted from http://stackoverflow.com/questions/13600739/calculate-centre-of-sphere-whose-surface-contains-4-points-c
        /// </summary>
        class CircumcentreSolver
        {
            private double x, y, z;
            private double radius;
            private double[,] p = { { 0, 0, 0 }, { 0, 0, 0 }, { 0, 0, 0 }, { 0, 0, 0 } };

            internal Point3d Center => new Point3d(x, y, z);
            internal double Radius => radius;

            /// <summary>
            /// Computes the centre of a sphere such that all four specified points in
            /// 3D space lie on the sphere's surface.
            /// </summary>
            /// <param name="a">The first point (array of 3 doubles for X, Y, Z).</param>
            /// <param name="b">The second point (array of 3 doubles for X, Y, Z).</param>
            /// <param name="c">The third point (array of 3 doubles for X, Y, Z).</param>
            /// <param name="d">The fourth point (array of 3 doubles for X, Y, Z).</param>
            internal CircumcentreSolver(Point3d pa, Point3d pb, Point3d pc, Point3d pd)
            {
                double[] a = new double[] { pa.X, pa.Y, pa.Z };
                double[] b = new double[] { pb.X, pb.Y, pb.Z };
                double[] c = new double[] { pc.X, pc.Y, pc.Z };
                double[] d = new double[] { pd.X, pd.Y, pd.Z };
                this.Compute(a, b, c, d);
            }

            /// <summary>
            /// Evaluate the determinant.
            /// </summary>
            void Compute(double[] a, double[] b, double[] c, double[] d)
            {
                p[0, 0] = a[0];
                p[0, 1] = a[1];
                p[0, 2] = a[2];
                p[1, 0] = b[0];
                p[1, 1] = b[1];
                p[1, 2] = b[2];
                p[2, 0] = c[0];
                p[2, 1] = c[1];
                p[2, 2] = c[2];
                p[3, 0] = d[0];
                p[3, 1] = d[1];
                p[3, 2] = d[2];

                // Compute result sphere.
                this.Sphere();
            }

            private void Sphere()
            {
                double m11, m12, m13, m14, m15;
                double[,] a = { { 0, 0, 0, 0 }, { 0, 0, 0, 0 }, { 0, 0, 0, 0 }, { 0, 0, 0, 0 } };

                // Find minor 1, 1.
                for (int i = 0; i < 4; i++)
                {
                    a[i, 0] = p[i, 0];
                    a[i, 1] = p[i, 1];
                    a[i, 2] = p[i, 2];
                    a[i, 3] = 1;
                }
                m11 = this.Determinant(a, 4);

                // Find minor 1, 2.
                for (int i = 0; i < 4; i++)
                {
                    a[i, 0] = p[i, 0] * p[i, 0] + p[i, 1] * p[i, 1] + p[i, 2] * p[i, 2];
                    a[i, 1] = p[i, 1];
                    a[i, 2] = p[i, 2];
                    a[i, 3] = 1;
                }
                m12 = this.Determinant(a, 4);

                // Find minor 1, 3.
                for (int i = 0; i < 4; i++)
                {
                    a[i, 0] = p[i, 0] * p[i, 0] + p[i, 1] * p[i, 1] + p[i, 2] * p[i, 2];
                    a[i, 1] = p[i, 0];
                    a[i, 2] = p[i, 2];
                    a[i, 3] = 1;
                }
                m13 = this.Determinant(a, 4);

                // Find minor 1, 4.
                for (int i = 0; i < 4; i++)
                {
                    a[i, 0] = p[i, 0] * p[i, 0] + p[i, 1] * p[i, 1] + p[i, 2] * p[i, 2];
                    a[i, 1] = p[i, 0];
                    a[i, 2] = p[i, 1];
                    a[i, 3] = 1;
                }
                m14 = this.Determinant(a, 4);

                // Find minor 1, 5.
                for (int i = 0; i < 4; i++)
                {
                    a[i, 0] = p[i, 0] * p[i, 0] + p[i, 1] * p[i, 1] + p[i, 2] * p[i, 2];
                    a[i, 1] = p[i, 0];
                    a[i, 2] = p[i, 1];
                    a[i, 3] = p[i, 2];
                }
                m15 = this.Determinant(a, 4);

                // Calculate result.
                if (m11 == 0)
                {
                    this.x = 0;
                    this.y = 0;
                    this.z = 0;
                    this.radius = 0;
                }
                else
                {
                    this.x = 0.5 * m12 / m11;
                    this.y = -0.5 * m13 / m11;
                    this.z = 0.5 * m14 / m11;
                    this.radius = Sqrt(this.x * this.x + this.y * this.y + this.z * this.z - m15 / m11);
                }
            }

            /// <summary>
            /// Recursive definition of determinate using expansion by minors.
            /// </summary>
            double Determinant(double[,] a, double n)
            {
                int i, j, j1, j2;
                double d = 0;
                double[,] m =
                        {
                    { 0, 0, 0, 0 },
                    { 0, 0, 0, 0 },
                    { 0, 0, 0, 0 },
                    { 0, 0, 0, 0 }
                };

                if (n == 2)
                {
                    // Terminate recursion.
                    d = a[0, 0] * a[1, 1] - a[1, 0] * a[0, 1];
                }
                else
                {
                    d = 0;
                    for (j1 = 0; j1 < n; j1++) // Do each column.
                    {
                        for (i = 1; i < n; i++) // Create minor.
                        {
                            j2 = 0;
                            for (j = 0; j < n; j++)
                            {
                                if (j == j1) continue;
                                m[i - 1, j2] = a[i, j];
                                j2++;
                            }
                        }

                        // Sum (+/-)cofactor * minor.
                        d += Pow(-1.0, j1) * a[0, j1] * this.Determinant(m, n - 1);
                    }
                }

                return d;
            }
        }

        public override string ToString() => $"Tool ({Name})";
    }

    public class Speed : TargetAttribute
    {
        /// <summary>
        /// Translation speed in mm/s
        /// </summary>
        public double TranslationSpeed { get; set; }
        /// <summary>
        /// Rotation speed in rad/s
        /// </summary>
        public double RotationSpeed { get; set; }
        /// <summary>
        /// Axis/joint speed override for joint motions as a factor (0 to 1) of the maximum speed (used in KUKA and UR)
        /// </summary>
        public double AxisSpeed { get { return axisSpeed; } set { axisSpeed = Clamp(value, 0, 1); } }
        private double axisSpeed = 1;
        /// <summary>
        /// Translation acceleration in mm/s² (used in UR)
        /// </summary>
        public double TranslationAccel { get; set; } = 1000;
        /// <summary>
        /// Axis/join acceleration in rads/s² (used in UR)
        /// </summary>
        public double AxisAccel { get; set; } = PI;

        /// <summary>
        /// Time in seconds it takes to reach the target. Optional parameter (used in UR)
        /// </summary>
        public double Time { get; set; } = 0;

        public static Speed Default { get; }

        static Speed()
        {
            Default = new Speed(100, PI, "DefaultSpeed");
        }

        public Speed(double translation = 100, double rotationSpeed = PI / 2, string name = null)
        {
            this.Name = name;
            this.TranslationSpeed = translation;
            this.RotationSpeed = rotationSpeed;
        }

        public override string ToString() => (Name != null) ? $"Speed ({Name})" : $"Speed ({TranslationSpeed:0.0} mm/s)";
    }

    public class Zone : TargetAttribute
    {
        /// <summary>
        /// Radius of the TCP zone in mm
        /// </summary>
        public double Distance { get; set; }
        /// <summary>
        /// The zone size for the tool reorientation in radians.
        /// </summary>
        public double Rotation { get; set; }
        public bool IsFlyBy => Distance > DistanceTol;

        public static Zone Default { get; }

        static Zone()
        {
            Default = new Zone(1, "DefaultZone");
        }

        public Zone(double distance, string name = null)
        {
            this.Name = name;
            this.Distance = distance;
            this.Rotation = (distance / 10).ToRadians();
        }

        public override string ToString() => (Name != null) ? $"Zone ({Name})" : IsFlyBy ? $"Zone ({Distance:0.00} mm)" : $"Zone (Stop point)";
    }

    public class Frame : TargetAttribute
    {
        /// <summary>
        /// Reference frame of plane for a target
        /// </summary>
        public Plane Plane { get; set; }
        public int CoupledMechanism { get; set; }
        public int CoupledMechanicalGroup { get; set; }
        public bool IsCoupled { get { return (CoupledMechanism != -1 || CoupledMechanicalGroup != -1); } }

        public static Frame Default { get; }

        static Frame()
        {
            Default = new Frame(Plane.WorldXY, -1, -1, "DefaultFrame");
        }

        public Frame(Plane plane, int coupledMechanism = -1, int coupledMechanicalGroup = -1, string name = null)
        {
            this.Name = name;
            this.Plane = plane;
            this.CoupledMechanism = coupledMechanism;
            this.CoupledMechanicalGroup = coupledMechanicalGroup;
        }

        public Frame ShallowClone() => MemberwiseClone() as Frame;

        public override string ToString() => (Name != null) ? $"Frame ({Name})" : $"Frame ({Plane.Origin}" + (IsCoupled ? " Coupled" : "") + ")";
    }
}