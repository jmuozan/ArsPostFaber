#include "export.h"
#include <CGAL/Simple_cartesian.h>
#include <CGAL/Surface_mesh.h>
#include <CGAL/Polygon_mesh_processing/clip.h>

extern "C" {
CGAL_WRAPPER_API bool SliceMesh(
    const double* verts, int vertCount,
    const int* tris, int triCount,
    const double* planeO, const double* planeN,
    double** outVerts, int* outV,
    int** outIdx, int* outT)
{
    using Kernel = CGAL::Simple_cartesian<double>;
    using Point = Kernel::Point_3;
    using SurfaceMesh = CGAL::Surface_mesh<Point>;

    SurfaceMesh M;
    // Build vertices
    for (int i = 0; i < vertCount; i += 3)
        M.add_vertex(Point(verts[i], verts[i+1], verts[i+2]));

    // Build faces
    for (int i = 0; i < triCount; i += 3)
        M.add_face(tris[i], tris[i+1], tris[i+2]);

    // Define slicing plane
    Kernel::Plane_3 P(
        Point(planeO[0], planeO[1], planeO[2]),
        Kernel::Vector_3(planeN[0], planeN[1], planeN[2]));

    namespace PMP = CGAL::Polygon_mesh_processing;
    PMP::clip(M, P);

    // TODO: Marshal clipped mesh back into outVerts/outIdx
    *outVerts = nullptr;
    *outIdx   = nullptr;
    *outV     = 0;
    *outT     = 0;
    return true;
}
} // extern "C"