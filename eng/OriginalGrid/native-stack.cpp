// Owned LibreWinForms startup stack: inherited process handle only, no debug attach.
// SDK structures supply the actual x64/ARM64 CONTEXT and DEBUG_EVENT ABI.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#define _WIN32_WINNT 0x0A00
#include <windows.h>
#include <dbghelp.h>
#include <cstdint>
#include <climits>
#include <cstdio>
#include <cstdlib>
#include <filesystem>
#include <initializer_list>
#include <string>
#include <type_traits>

namespace {
constexpr LONGLONG maximum_dump_bytes = 32 * 1024 * 1024;
#if defined(_M_ARM64)
constexpr USHORT machine = IMAGE_FILE_MACHINE_ARM64;
#elif defined(_M_X64)
constexpr USHORT machine = IMAGE_FILE_MACHINE_AMD64;
#else
#error This diagnostic supports native Windows ARM64 and x64 only.
#endif

struct handle {
    HANDLE value = nullptr;
    handle() = default;
    explicit handle(HANDLE input) noexcept : value(input) {}
    handle(const handle&) = delete;
    handle& operator=(const handle&) = delete;
    ~handle() { if (value && value != INVALID_HANDLE_VALUE) CloseHandle(value); }
    bool valid() const noexcept { return value && value != INVALID_HANDLE_VALUE; }
};

struct dump_budget { HANDLE file; ULONGLONG deadline; };
BOOL CALLBACK check_dump_budget(void* parameter, PMINIDUMP_CALLBACK_INPUT input,
                               MINIDUMP_CALLBACK_OUTPUT* output) {
    switch (input->CallbackType) {
        case ModuleCallback:
        case ThreadCallback:
        case ThreadExCallback:
        case IncludeThreadCallback:
        case IncludeModuleCallback:
            return TRUE; // Preserve the SDK's default write flags and inclusion.
        case CancelCallback: {
            const auto& budget = *static_cast<const dump_budget*>(parameter);
            LARGE_INTEGER size{};
            output->CheckCancel = TRUE;
            output->Cancel = GetTickCount64() >= budget.deadline ||
                !GetFileSizeEx(budget.file, &size) || size.QuadPart > maximum_dump_bytes;
            return TRUE;
        }
        case VmStartCallback:
            output->Status = S_OK; // No alternate virtual-memory reader.
            return TRUE;
        default:
            // Do not opt into alternate I/O, snapshot handles, extra memory or
            // kernel dumps, and never acknowledge an ignored memory-read error.
            return FALSE;
    }
}
static_assert(std::is_same_v<decltype(&check_dump_budget), MINIDUMP_CALLBACK_ROUTINE>);

bool callback_controls() {
    MINIDUMP_CALLBACK_INPUT input{};
    MINIDUMP_CALLBACK_OUTPUT output{};
    constexpr ULONG sentinel = 0x12345678;
    for (const auto kind : {ModuleCallback, ThreadCallback, ThreadExCallback,
                           IncludeThreadCallback, IncludeModuleCallback}) {
        input.CallbackType = kind;
        output.ModuleWriteFlags = sentinel;
        if (!check_dump_budget(nullptr, &input, &output) || output.ModuleWriteFlags != sentinel) return false;
    }
    for (const auto kind : {MemoryCallback, WriteKernelMinidumpCallback, RemoveMemoryCallback,
                           IoStartCallback, IoWriteAllCallback, IoFinishCallback,
                           ReadMemoryFailureCallback, IsProcessSnapshotCallback, SecondaryFlagsCallback}) {
        input.CallbackType = kind;
        output.ModuleWriteFlags = sentinel;
        if (check_dump_budget(nullptr, &input, &output) || output.ModuleWriteFlags != sentinel) return false;
    }
    input.CallbackType = MAXDWORD;
    if (check_dump_budget(nullptr, &input, &output) || output.ModuleWriteFlags != sentinel) return false;
    input.CallbackType = VmStartCallback;
    output.Status = E_NOTIMPL;
    if (!check_dump_budget(nullptr, &input, &output) || output.Status != S_OK) return false;
    input.CallbackType = CancelCallback;
    dump_budget expired{INVALID_HANDLE_VALUE, 0};
    return check_dump_budget(&expired, &input, &output) && output.CheckCancel && output.Cancel;
}

DWORD write_minidump(HANDLE process, DWORD pid, const std::filesystem::path& path,
                     MINIDUMP_EXCEPTION_INFORMATION* exception) {
    handle file(CreateFileW(path.c_str(), GENERIC_WRITE | GENERIC_READ, 0, nullptr,
                            CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr));
    if (!file.valid()) return GetLastError();
    dump_budget budget{file.value, GetTickCount64() + 5000};
    MINIDUMP_CALLBACK_INFORMATION callback{check_dump_budget, &budget};
    if (!MiniDumpWriteDump(process, pid, file.value, MiniDumpNormal,
                           exception, nullptr, &callback)) return GetLastError();
    LARGE_INTEGER size{};
    if (!GetFileSizeEx(file.value, &size)) return GetLastError();
    return size.QuadPart >= 32 && size.QuadPart <= maximum_dump_bytes ? ERROR_SUCCESS : ERROR_FILE_TOO_LARGE;
}

} // namespace

int wmain(int argc, wchar_t** argv) {
    if (argc == 2 && std::wstring(argv[1]) == L"--self-test") return callback_controls() ? 0 : 10;
    if (argc != 4) return 2;
    wchar_t* end = nullptr;
    const auto raw = wcstoull(argv[1], &end, 10);
    if (!end || *end || !raw || raw == ULLONG_MAX) return 3;
    const auto expected = wcstoul(argv[2], &end, 10);
    if (!end || *end || !expected) return 3;
    handle process(reinterpret_cast<HANDLE>(static_cast<uintptr_t>(raw)));
    if (!process.valid() || GetProcessId(process.value) != expected ||
        WaitForSingleObject(process.value, 0) != WAIT_TIMEOUT) return 4;
    // The caller duplicates only its still-owned Popen handle with read rights.
    // This helper never discovers, opens, suspends, terminates or debugs a PID.
    const auto error = write_minidump(process.value, expected, argv[3], nullptr);
    fwprintf(stderr, L"MiniDumpNormal error=%lu machine=%u pid=%lu\n", error, machine, expected);
    return error == ERROR_SUCCESS ? 0 : 5;
}
