#include "export.h"
#include <CGAL/Simple_cartesian.h>
#include <CGAL/Surface_mesh.h>
#include <CGAL/Polygon_mesh_processing/section.h>
#include <vector>
#include <list>
#include <cstdlib>
#include <cstring>

extern "C" {
// Allocates memory via malloc; caller must free with FreeBuffer
CGAL_WRAPPER_API bool SliceMesh(
    const double* verts, int vertCount,
    const int* tris, int triCount,
    const double* planeO, const double* planeN,
    double** outPts, int* outPtCount,
    int** outOffsets, int* outLoopCount)
{
    using Kernel = CGAL::Simple_cartesian<double>;
    using Point = Kernel::Point_3;
    using SurfaceMesh = CGAL::Surface_mesh<Point>;
    namespace PMP = CGAL::Polygon_mesh_processing;

    // Build CGAL mesh
    SurfaceMesh M;
    for (int i = 0; i < vertCount; i += 3) {
        M.add_vertex(Point(verts[i], verts[i+1], verts[i+2]));
    }
    for (int i = 0; i < triCount; i += 3) {
        M.add_face(tris[i], tris[i+1], tris[i+2]);
    }

    // Define slicing plane
    Kernel::Plane_3 P(
        Point(planeO[0], planeO[1], planeO[2]),
        Kernel::Vector_3(planeN[0], planeN[1], planeN[2]));

    // Compute section contours: list of vertex loops
    std::list<std::vector<Point>> loops;
    PMP::section(M, P, std::back_inserter(loops));

    int L = static_cast<int>(loops.size());
    if (L == 0) {
        *outPts = nullptr;
        *outOffsets = nullptr;
        *outPtCount = 0;
        *outLoopCount = 0;
        return true;
    }

    // Compute offsets
    std::vector<int> offsets(L + 1, 0);
    for (int i = 0; i < L; ++i) {
        offsets[i+1] = offsets[i] + static_cast<int>(std::next(loops.begin(), i)->size());
    }
    int totalPts = offsets[L];

    // Allocate output buffers
    double* pts = static_cast<double*>(std::malloc(sizeof(double) * totalPts * 3));
    int* offs = static_cast<int*>(std::malloc(sizeof(int) * (L + 1)));
    if (!pts || !offs) {
        std::free(pts);
        std::free(offs);
        return false;
    }
    std::memcpy(offs, offsets.data(), sizeof(int) * (L + 1));

    // Fill point array
    int idx = 0;
    for (auto& loop : loops) {
        for (auto& pt : loop) {
            pts[idx++] = pt.x();
            pts[idx++] = pt.y();
            pts[idx++] = pt.z();
        }
    }

    *outPts = pts;
    *outOffsets = offs;
    *outPtCount = totalPts;
    *outLoopCount = L;
    return true;
}

// Frees memory allocated by SliceMesh
CGAL_WRAPPER_API void FreeBuffer(void* ptr) {
    std::free(ptr);
}
} // extern "C"