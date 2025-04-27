/**
 * MediaPipe C API Header
 */
#ifndef MEDIAPIPE_C_API_H_
#define MEDIAPIPE_C_API_H_

#ifdef __cplusplus
extern "C" {
#endif

/**
 * Initializes a MediaPipe graph from a graph config file.
 * Returns a pointer to the graph, or NULL on error.
 */
void* mp_create_graph(const char* graph_config);

/**
 * Destroys a MediaPipe graph.
 */
void mp_destroy_graph(void* graph);

/**
 * Starts running a MediaPipe graph.
 * Returns 0 on success, non-zero on error.
 */
int mp_start_graph(void* graph);

/**
 * Stops running a MediaPipe graph.
 * Returns 0 on success, non-zero on error.
 */
int mp_stop_graph(void* graph);

/**
 * Processes a frame through a MediaPipe graph.
 * Returns 0 on success, non-zero on error.
 */
int mp_process_frame(void* graph, const unsigned char* image_data, 
                    int width, int height, int step, int format);

#ifdef __cplusplus
}
#endif

#endif  // MEDIAPIPE_C_API_H_
