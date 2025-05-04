#pragma once

#if defined(_WIN32) || defined(__CYGWIN__)
#  ifdef CGAL_WRAPPER_EXPORTS
#    define CGAL_WRAPPER_API __declspec(dllexport)
#  else
#    define CGAL_WRAPPER_API __declspec(dllimport)
#  endif
#else
#  define CGAL_WRAPPER_API __attribute__((visibility("default")))
#endif