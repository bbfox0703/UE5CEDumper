// [AOB-SKIP-OWN-DLL] dll_core_test's stand-ins for another build of the dumper mapped beside this one.
//
// dll/CMakeLists.txt builds this file twice, as code-free DLLs linked /NOENTRY:
//   owncopy_dumper    exports the mailbox contract with its real magic -- what every build of ours exports
//   owncopy_impostor  exports the same NAME with a wrong magic -- a module that merely shares the name
// Both carry the same marker at the start of an executable section larger than the walk's size floor, so the walk
// would find it in each; whether it is reported from one is the decision under test, not the pattern's presence.

#include "../../src/Mimic.h"

#pragma section(".ownfix", read, execute)

// The first bytes are the marker dll_core_test scans for; the rest only lifts the image past kMinScannedModuleSize.
extern "C" __declspec(dllexport) __declspec(allocate(".ownfix")) const unsigned char kOwnCopyFixtureCode[0x11000] = {
    0xE8, 0x4F, 0x57, 0x4E, 0x43, 0x4F, 0x50, 0x59, 0x9C, 0x37, 0xB1, 0xD4, 0x2A, 0x6E, 0x05, 0xF3,
};

#ifdef OWNCOPY_FIXTURE_IMPOSTOR
extern "C" __declspec(dllexport) Mimic::MailboxContract g_mailboxContract = {
    ~Mimic::MAILBOX_CONTRACT_MAGIC, Mimic::MAILBOX_CONTRACT, Mimic::MAILBOX_CONTRACT_MIN,
};
#else
extern "C" __declspec(dllexport) Mimic::MailboxContract g_mailboxContract = {
    Mimic::MAILBOX_CONTRACT_MAGIC, Mimic::MAILBOX_CONTRACT, Mimic::MAILBOX_CONTRACT_MIN,
};
#endif
