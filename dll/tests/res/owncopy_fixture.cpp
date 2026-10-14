// [AOB-SKIP-OWN-DLL] dll_core_test's stand-ins for another build of the dumper mapped beside this one.
//
// dll/CMakeLists.txt builds this file three times, as DLLs linked /NOENTRY (no CRT):
//   owncopy_dumper    exports the mailbox contract with its real magic -- what every build of ours exports today
//   owncopy_impostor  exports the same NAME with a wrong magic -- a module that merely shares the name
//   owncopy_legacy    exports no contract, only UE5_Init -- a build from before the contract existed
// All carry the same marker at the start of an executable section larger than the walk's size floor, so the walk
// would find it in each; whether it is reported from one is the decision under test, not the pattern's presence.

#include "../../src/Mimic.h"

#pragma section(".ownfix", read, execute)

// The first bytes are the marker dll_core_test scans for; the rest only lifts the image past kMinScannedModuleSize.
extern "C" __declspec(dllexport) __declspec(allocate(".ownfix")) const unsigned char kOwnCopyFixtureCode[0x11000] = {
    0xE8, 0x4F, 0x57, 0x4E, 0x43, 0x4F, 0x50, 0x59, 0x9C, 0x37, 0xB1, 0xD4, 0x2A, 0x6E, 0x05, 0xF3,
};

#if defined(OWNCOPY_FIXTURE_LEGACY)
// The C ABI's init entry, exported by every build since the first; the contract only since 2026-08-06.
extern "C" __declspec(dllexport) bool UE5_Init() { return false; }
#elif defined(OWNCOPY_FIXTURE_IMPOSTOR)
extern "C" __declspec(dllexport) Mimic::MailboxContract g_mailboxContract = {
    ~Mimic::MAILBOX_CONTRACT_MAGIC, Mimic::MAILBOX_CONTRACT, Mimic::MAILBOX_CONTRACT_MIN,
};
#else
extern "C" __declspec(dllexport) Mimic::MailboxContract g_mailboxContract = {
    Mimic::MAILBOX_CONTRACT_MAGIC, Mimic::MAILBOX_CONTRACT, Mimic::MAILBOX_CONTRACT_MIN,
};
#endif
