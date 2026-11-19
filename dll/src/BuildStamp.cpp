// ============================================================
// BuildStamp.cpp — the C++ translation unit that includes the CMake-generated
// BuildInfo.h; other TUs include BuildStamp.h. Keeping the volatile build
// macros behind these out-of-line accessors means a per-build number bump
// recompiles just this one tiny file (and version.rc), not the heavy
// consumers. See BuildStamp.h.
// ============================================================
#include "BuildStamp.h"
#include "BuildInfo.h"

namespace BuildStamp {
    const char* VersionString() { return BUILD_VERSION_STRING; }
    const char* GitShort()      { return BUILD_GIT_SHORT; }
    const char* GitHash()       { return BUILD_GIT_HASH; }
    const char* Timestamp()     { return BUILD_TIMESTAMP; }
    const char* Config()        { return BUILD_CONFIG; }
    int         BuildNumber()   { return static_cast<int>(VER_BUILD); }
}
