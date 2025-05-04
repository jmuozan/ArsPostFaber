cmake_minimum_required(VERSION 3.1)
project(Crft.CGALWrapper LANGUAGES CXX)

# Use C++17
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

find_package(CGAL REQUIRED)

# Shared library target
add_library(Crft.CGALWrapper SHARED src/Crft.CGALWrapper.cpp)

# Symbols export definition
target_compile_definitions(Crft.CGALWrapper PRIVATE CGAL_WRAPPER_EXPORTS)

# Include CGAL headers
target_include_directories(Crft.CGALWrapper PRIVATE ${CGAL_INCLUDE_DIRS})

# Link CGAL
target_link_libraries(Crft.CGALWrapper PRIVATE CGAL::CGAL)

# Ensure correct naming for shared library
if(APPLE)
  set_target_properties(Crft.CGALWrapper PROPERTIES PREFIX "lib" SUFFIX ".dylib")
elseif(WIN32)
  set_target_properties(Crft.CGALWrapper PROPERTIES OUTPUT_NAME "Crft.CGALWrapper" SUFFIX ".dll")
endif()