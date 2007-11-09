using System;
using System.Collections;
using System.Collections.Generic;
using GeoAPI.Coordinates;
using GeoAPI.Geometries;
using GisSharpBlog.NetTopologySuite.Algorithm;
using GisSharpBlog.NetTopologySuite.GeometriesGraph;
using GisSharpBlog.NetTopologySuite.Utilities;
using NPack.Interfaces;

namespace GisSharpBlog.NetTopologySuite.Operation.Overlay
{
    /// <summary>
    /// Forms <c>Polygon</c>s out of a graph of {DirectedEdge}s.
    /// The edges to use are marked as being in the result Area.
    /// </summary>
    public class PolygonBuilder<TCoordinate>
        where TCoordinate : ICoordinate, IEquatable<TCoordinate>, IComparable<TCoordinate>,
                            IComputable<TCoordinate>, IConvertible
    {
        private readonly IGeometryFactory<TCoordinate> _geometryFactory;
        private readonly List<ILinearRing<TCoordinate>> _shellList = new List<ILinearRing<TCoordinate>>();

        public PolygonBuilder(IGeometryFactory<TCoordinate> geometryFactory)
        {
            this._geometryFactory = geometryFactory;
        }

        /// <summary>
        /// Add a complete graph.
        /// The graph is assumed to contain one or more polygons,
        /// possibly with holes.
        /// </summary>
        public void Add(PlanarGraph graph)
        {
            Add(graph.EdgeEnds, graph.Nodes);
        }

        /// <summary> 
        /// Add a set of edges and nodes, which form a graph.
        /// The graph is assumed to contain one or more polygons,
        /// possibly with holes.
        /// </summary>
        public void Add(IEnumerable<Edge> dirEdges, IList nodes)
        {
            PlanarGraph.LinkResultDirectedEdges(nodes);
            IList maxEdgeRings = BuildMaximalEdgeRings(dirEdges);
            IList freeHoleList = new ArrayList();
            IList edgeRings = BuildMinimalEdgeRings(maxEdgeRings, _shellList, freeHoleList);
            SortShellsAndHoles(edgeRings, _shellList, freeHoleList);
            PlaceFreeHoles(_shellList, freeHoleList);
            //Assert: every hole on freeHoleList has a shell assigned to it
        }

        public IList Polygons
        {
            get
            {
                IList resultPolyList = ComputePolygons(_shellList);
                return resultPolyList;
            }
        }

        /// <summary> 
        /// For all DirectedEdges in result, form them into MaximalEdgeRings.
        /// </summary>
        private IList BuildMaximalEdgeRings(IList dirEdges)
        {
            IList maxEdgeRings = new ArrayList();
            for (IEnumerator it = dirEdges.GetEnumerator(); it.MoveNext();)
            {
                DirectedEdge de = (DirectedEdge) it.Current;
                if (de.IsInResult && de.Label.IsArea())
                {
                    // if this edge has not yet been processed
                    if (de.EdgeRing == null)
                    {
                        MaximalEdgeRing er = new MaximalEdgeRing(de, _geometryFactory);
                        maxEdgeRings.Add(er);
                        er.SetInResult();
                    }
                }
            }
            return maxEdgeRings;
        }

        private IList BuildMinimalEdgeRings(IList maxEdgeRings, IList shellList, IList freeHoleList)
        {
            IList edgeRings = new ArrayList();
            for (IEnumerator it = maxEdgeRings.GetEnumerator(); it.MoveNext();)
            {
                MaximalEdgeRing er = (MaximalEdgeRing) it.Current;
                if (er.MaxNodeDegree > 2)
                {
                    er.LinkDirectedEdgesForMinimalEdgeRings();
                    IList minEdgeRings = er.BuildMinimalRings();
                    // at this point we can go ahead and attempt to place holes, if this EdgeRing is a polygon
                    EdgeRing shell = FindShell(minEdgeRings);
                    if (shell != null)
                    {
                        PlacePolygonHoles(shell, minEdgeRings);
                        shellList.Add(shell);
                    }
                    else
                    {
                        // freeHoleList.addAll(minEdgeRings);
                        foreach (object obj in minEdgeRings)
                        {
                            freeHoleList.Add(obj);
                        }
                    }
                }
                else
                {
                    edgeRings.Add(er);
                }
            }
            return edgeRings;
        }

        /// <summary>
        /// This method takes a list of MinimalEdgeRings derived from a MaximalEdgeRing,
        /// and tests whether they form a Polygon.  This is the case if there is a single shell
        /// in the list.  In this case the shell is returned.
        /// The other possibility is that they are a series of connected holes, in which case
        /// no shell is returned.
        /// </summary>
        /// <returns>The shell EdgeRing, if there is one.</returns>
        /// <returns><see langword="null" />, if all the rings are holes.</returns>
        private EdgeRing FindShell(IList minEdgeRings)
        {
            Int32 shellCount = 0;
            EdgeRing shell = null;
            for (IEnumerator it = minEdgeRings.GetEnumerator(); it.MoveNext();)
            {
                EdgeRing er = (MinimalEdgeRing) it.Current;
                if (!er.IsHole)
                {
                    shell = er;
                    shellCount++;
                }
            }
            Assert.IsTrue(shellCount <= 1, "found two shells in MinimalEdgeRing list");
            return shell;
        }

        /// <summary>
        /// This method assigns the holes for a Polygon (formed from a list of
        /// MinimalEdgeRings) to its shell.
        /// Determining the holes for a MinimalEdgeRing polygon serves two purposes:
        /// it is faster than using a point-in-polygon check later on.
        /// it ensures correctness, since if the PIP test was used the point
        /// chosen might lie on the shell, which might return an incorrect result from the
        /// PIP test.
        /// </summary>
        private void PlacePolygonHoles(EdgeRing shell, IList minEdgeRings)
        {
            for (IEnumerator it = minEdgeRings.GetEnumerator(); it.MoveNext();)
            {
                MinimalEdgeRing er = (MinimalEdgeRing) it.Current;
                if (er.IsHole)
                {
                    er.Shell = shell;
                }
            }
        }

        /// <summary> 
        /// For all rings in the input list,
        /// determine whether the ring is a shell or a hole
        /// and add it to the appropriate list.
        /// Due to the way the DirectedEdges were linked,
        /// a ring is a shell if it is oriented CW, a hole otherwise.
        /// </summary>
        private void SortShellsAndHoles(IList edgeRings, IList shellList, IList freeHoleList)
        {
            for (IEnumerator it = edgeRings.GetEnumerator(); it.MoveNext();)
            {
                EdgeRing er = (EdgeRing) it.Current;
                er.SetInResult();
                if (er.IsHole)
                {
                    freeHoleList.Add(er);
                }
                else
                {
                    shellList.Add(er);
                }
            }
        }

        /// <summary>
        /// This method determines finds a containing shell for all holes
        /// which have not yet been assigned to a shell.
        /// These "free" holes should
        /// all be properly contained in their parent shells, so it is safe to use the
        /// <c>findEdgeRingContaining</c> method.
        /// (This is the case because any holes which are NOT
        /// properly contained (i.e. are connected to their
        /// parent shell) would have formed part of a MaximalEdgeRing
        /// and been handled in a previous step).
        /// </summary>
        private void PlaceFreeHoles(IList shellList, IList freeHoleList)
        {
            for (IEnumerator it = freeHoleList.GetEnumerator(); it.MoveNext();)
            {
                EdgeRing hole = (EdgeRing) it.Current;
                // only place this hole if it doesn't yet have a shell
                if (hole.Shell == null)
                {
                    EdgeRing shell = FindEdgeRingContaining(hole, shellList);
                    Assert.IsTrue(shell != null, "unable to assign hole to a shell");
                    hole.Shell = shell;
                }
            }
        }

        /// <summary> 
        /// Find the innermost enclosing shell EdgeRing containing the argument EdgeRing, if any.
        /// The innermost enclosing ring is the <i>smallest</i> enclosing ring.
        /// The algorithm used depends on the fact that:
        /// ring A contains ring B iff envelope(ring A) contains envelope(ring B).
        /// This routine is only safe to use if the chosen point of the hole
        /// is known to be properly contained in a shell
        /// (which is guaranteed to be the case if the hole does not touch its shell).
        /// </summary>
        /// <returns>Containing EdgeRing, if there is one, OR
        /// null if no containing EdgeRing is found.</returns>
        private EdgeRing FindEdgeRingContaining(EdgeRing testEr, IList shellList)
        {
            ILinearRing teString = testEr.LinearRing;
            IExtents testEnv = teString.EnvelopeInternal;
            ICoordinate testPt = teString.GetCoordinateN(0);

            EdgeRing minShell = null;
            IExtents minEnv = null;
            for (IEnumerator it = shellList.GetEnumerator(); it.MoveNext();)
            {
                EdgeRing tryShell = (EdgeRing) it.Current;
                ILinearRing tryRing = tryShell.LinearRing;
                IExtents tryEnv = tryRing.EnvelopeInternal;
                if (minShell != null)
                {
                    minEnv = minShell.LinearRing.EnvelopeInternal;
                }
                Boolean isContained = false;
                if (tryEnv.Contains(testEnv) && CGAlgorithms.IsPointInRing(testPt, tryRing.Coordinates))
                {
                    isContained = true;
                }
                // check if this new containing ring is smaller than the current minimum ring
                if (isContained)
                {
                    if (minShell == null || minEnv.Contains(tryEnv))
                    {
                        minShell = tryShell;
                    }
                }
            }
            return minShell;
        }

        private IList ComputePolygons(IList shellList)
        {
            IList resultPolyList = new ArrayList();
            // add Polygons for all shells
            for (IEnumerator it = shellList.GetEnumerator(); it.MoveNext();)
            {
                EdgeRing er = (EdgeRing) it.Current;
                IPolygon poly = er.ToPolygon(_geometryFactory);
                resultPolyList.Add(poly);
            }
            return resultPolyList;
        }

        /// <summary> 
        /// Checks the current set of shells (with their associated holes) to
        /// see if any of them contain the point.
        /// </summary>
        public Boolean ContainsPoint(ICoordinate p)
        {
            for (IEnumerator it = _shellList.GetEnumerator(); it.MoveNext();)
            {
                EdgeRing er = (EdgeRing) it.Current;
                if (er.ContainsPoint(p))
                {
                    return true;
                }
            }
            return false;
        }
    }
}