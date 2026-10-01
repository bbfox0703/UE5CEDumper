/* The test host for Cheat Engine's Lua VM: run one script file on the installed lua53-64.dll.
 *
 *     lua53ce.exe script.lua [args...]
 *
 * WHY IT DECLARES THE API ITSELF. It includes no Lua header. Building against Lua's own lua.c meant
 * taking lua.c and six headers from Cheat Engine's source tree -- two of them modified by CE, one
 * written by it, in a repository that states no licence -- so every machine needed a clone of that
 * tree, and this repo could not carry the files. The host uses a small part of the public C API;
 * the prototypes below are that part as the Lua 5.3 reference manual gives it, and
 * ce_lua53_host.py refuses to build if the installed DLL does not export every one of them.
 *
 * WHAT IT DOES, which is what a test needs from `lua script.lua`: open the standard libraries, set
 * the global `arg` (arg[0] the script, arg[1..] its arguments, arg[-1] this exe), run the script with
 * its arguments as `...`, and on an error print `<exe>: <message>` with a traceback to stderr and
 * exit 1. It reads no LUA_INIT and takes no options: a test must not depend on the environment.
 *
 * The script runs inside a protected C function, as the stock interpreter runs it, so an error's
 * traceback ends in the same `[C]: in ?` frame. Measured against the lua.c-built host this replaced
 * (2026-10-01): stdout, stderr and exit code identical on all twelve suites in scripts/tests and on
 * nine error / argument / exit-code cases.
 *
 * THE TYPES ARE AN ASSUMPTION, AND IT IS CHECKED. lua_Integer is written here as a 64-bit integer
 * and lua_Number as a double. A Lua 5.3 can be built otherwise (LUA_32BITS, LUA_C89_NUMBERS), so the
 * first thing the host does is hand both sizes to the DLL's own luaL_checkversion_, which raises if
 * the DLL was built with different ones or is not 5.3. lua_KContext only ever carries 0 here, and
 * lua_State is opaque. */
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>

typedef struct lua_State lua_State;
typedef long long lua_Integer;
typedef intptr_t lua_KContext;
typedef int (*lua_CFunction)(lua_State *L);
typedef int (*lua_KFunction)(lua_State *L, int status, lua_KContext ctx);

#define LUA_OK 0
#define LUA_MULTRET (-1)
#define LUA_TSTRING 4
#define LUA_VERSION_NUM 503
/* What lauxlib.h calls LUAL_NUMSIZES: the two sizes luaL_checkversion_ compares with the DLL's. */
#define NUMSIZES (sizeof(lua_Integer) * 16 + sizeof(double))
#define API __declspec(dllimport)

API lua_State *luaL_newstate(void);
API void luaL_checkversion_(lua_State *L, double ver, size_t sz);
API void luaL_openlibs(lua_State *L);
API int luaL_loadfilex(lua_State *L, const char *filename, const char *mode);
API void luaL_traceback(lua_State *L, lua_State *L1, const char *msg, int level);
API int luaL_callmeta(lua_State *L, int obj, const char *e);
API void lua_close(lua_State *L);
API int lua_checkstack(lua_State *L, int n);
API int lua_type(lua_State *L, int idx);
API const char *lua_typename(lua_State *L, int tp);
API const char *lua_tolstring(lua_State *L, int idx, size_t *len);
API const char *lua_pushstring(lua_State *L, const char *s);
API const char *lua_pushfstring(lua_State *L, const char *fmt, ...);
API void lua_pushcclosure(lua_State *L, lua_CFunction fn, int n);
API void lua_createtable(lua_State *L, int narr, int nrec);
API void lua_rawseti(lua_State *L, int idx, lua_Integer n);
API void lua_setglobal(lua_State *L, const char *name);
API int lua_pcallk(lua_State *L, int nargs, int nresults, int msgh, lua_KContext ctx,
                   lua_KFunction k);

static int g_argc;
static char **g_argv;
static int g_failed;

/* The message handler: a string error gets a traceback; anything else is turned into a string by
 * its __tostring, or named by its type, so there is always text to print. */
static int traceback(lua_State *L) {
    const char *msg = lua_tolstring(L, 1, NULL);
    if (lua_type(L, 1) != LUA_TSTRING) {
        if (luaL_callmeta(L, 1, "__tostring") && lua_type(L, -1) == LUA_TSTRING)
            return 1;
        msg = lua_pushfstring(L, "(error object is a %s value)", lua_typename(L, lua_type(L, 1)));
    }
    luaL_traceback(L, L, msg, 1);
    return 1;
}

static void report(lua_State *L) {
    const char *msg = lua_tolstring(L, -1, NULL);
    fflush(stdout);
    fprintf(stderr, "%s: %s\n", g_argv[0], msg ? msg : "(error object is not a string)");
    fflush(stderr);
    g_failed = 1;
}

/* Everything that touches the script, as one protected call. */
static int pmain(lua_State *L) {
    int i;
    luaL_checkversion_(L, LUA_VERSION_NUM, NUMSIZES);
    luaL_openlibs(L);
    lua_createtable(L, g_argc - 2, 2);
    for (i = 0; i < g_argc; i++) {
        lua_pushstring(L, g_argv[i]);
        lua_rawseti(L, -2, (lua_Integer)i - 1);          /* the script is argv[1] and arg[0] */
    }
    lua_setglobal(L, "arg");

    lua_pushcclosure(L, traceback, 0);                   /* stack: handler */
    if (luaL_loadfilex(L, g_argv[1], NULL) != LUA_OK) {  /* stack: handler, chunk | message */
        report(L);
        return 0;
    }
    if (!lua_checkstack(L, g_argc)) {
        lua_pushstring(L, "too many arguments to script");
        report(L);
        return 0;
    }
    for (i = 2; i < g_argc; i++)
        lua_pushstring(L, g_argv[i]);
    if (lua_pcallk(L, g_argc - 2, LUA_MULTRET, 1, 0, NULL) != LUA_OK)
        report(L);
    return 0;
}

int main(int argc, char **argv) {
    lua_State *L;
    g_argc = argc;
    g_argv = argv;
    if (argc < 2) {
        fprintf(stderr, "usage: %s script.lua [args...]\n", argv[0]);
        return EXIT_FAILURE;
    }
    L = luaL_newstate();
    if (L == NULL) {
        fprintf(stderr, "%s: cannot create a Lua state (not enough memory)\n", argv[0]);
        return EXIT_FAILURE;
    }
    lua_pushcclosure(L, pmain, 0);
    if (lua_pcallk(L, 0, 0, 0, 0, NULL) != LUA_OK)       /* an error outside the script's own call */
        report(L);
    fflush(stdout);
    lua_close(L);
    return g_failed ? EXIT_FAILURE : EXIT_SUCCESS;
}
