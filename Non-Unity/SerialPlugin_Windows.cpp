// SerialPlugin_Windows.cpp
// Windows implementation of Unity Serial Plugin
//
// Compile with Visual Studio or MinGW:
// cl /EHsc /MD /LD SerialPlugin_Windows.cpp /Fe:SerialPlugin.dll
// or
// g++ -shared -o SerialPlugin.dll SerialPlugin_Windows.cpp
//
// Place the resulting SerialPlugin.dll in a /Plugins folder in Unity

#include <windows.h>
#include <iostream>

#define EXPORT extern "C" __declspec(dllexport)

// --------------------
// State
// --------------------
static HANDLE hSerial = INVALID_HANDLE_VALUE;
static bool debugSerial = false;

// --------------------
// Convert baud index to actual baud rate
// --------------------
static DWORD baudToRate(unsigned char baudIndex) {
    switch (baudIndex) {
        case 0:     return CBR_4800;
        case 1:     return CBR_9600;
        case 2:     return CBR_19200;
        case 3:     return CBR_38400;
        case 4:     return CBR_57600;
        case 5:     return CBR_115200;
        default:    return CBR_300;
    }
}

// --------------------
// SerialOpen
// --------------------
EXPORT int SerialOpen(const char* portName, unsigned char baudIndex, bool debug) {
    debugSerial = debug;

    // Windows COM ports need to be prefixed with "\\\\.\\" for ports >= COM10
    char fullPortName[256];
    if (strncmp(portName, "COM", 3) == 0) {
        snprintf(fullPortName, sizeof(fullPortName), "\\\\.\\%s", portName);
    } else {
        strncpy(fullPortName, portName, sizeof(fullPortName) - 1);
        fullPortName[sizeof(fullPortName) - 1] = '\0';
    }

    hSerial = CreateFileA(
        fullPortName,
        GENERIC_READ | GENERIC_WRITE,
        0,      // No sharing
        NULL,   // No security
        OPEN_EXISTING,
        0,      // Not overlapped I/O
        NULL
    );

    if (hSerial == INVALID_HANDLE_VALUE) {
        if (debugSerial) std::cerr << "Failed to open port: " << GetLastError() << std::endl;
        return 0;
    }

    // Configure serial port parameters
    DCB dcbSerialParams = {0};
    dcbSerialParams.DCBlength = sizeof(dcbSerialParams);

    if (!GetCommState(hSerial, &dcbSerialParams)) {
        if (debugSerial) std::cerr << "Failed to get COM state" << std::endl;
        CloseHandle(hSerial);
        hSerial = INVALID_HANDLE_VALUE;
        return 0;
    }

    dcbSerialParams.BaudRate = baudToRate(baudIndex);
    dcbSerialParams.ByteSize = 8;
    dcbSerialParams.StopBits = ONESTOPBIT;
    dcbSerialParams.Parity = NOPARITY;
    dcbSerialParams.fDtrControl = DTR_CONTROL_ENABLE;

    if (!SetCommState(hSerial, &dcbSerialParams)) {
        if (debugSerial) std::cerr << "Failed to set COM state" << std::endl;
        CloseHandle(hSerial);
        hSerial = INVALID_HANDLE_VALUE;
        return 0;
    }

    // Set timeouts for non-blocking reads
    COMMTIMEOUTS timeouts = {0};
    timeouts.ReadIntervalTimeout = MAXDWORD;
    timeouts.ReadTotalTimeoutConstant = 0;
    timeouts.ReadTotalTimeoutMultiplier = 0;
    timeouts.WriteTotalTimeoutConstant = 50;
    timeouts.WriteTotalTimeoutMultiplier = 10;

    if (!SetCommTimeouts(hSerial, &timeouts)) {
        if (debugSerial) std::cerr << "Failed to set timeouts" << std::endl;
        CloseHandle(hSerial);
        hSerial = INVALID_HANDLE_VALUE;
        return 0;
    }

    // Clear buffers
    PurgeComm(hSerial, PURGE_RXCLEAR | PURGE_TXCLEAR);

    // Arduino CDC devices reboot on open → wait
    Sleep(20); // 20ms

    if (debugSerial) std::cout << "Serial port opened" << std::endl;
    return 1;
}

// --------------------
// SerialClose
// --------------------
EXPORT void SerialClose() {
    if (hSerial != INVALID_HANDLE_VALUE) {
        CloseHandle(hSerial);
        hSerial = INVALID_HANDLE_VALUE;
    }
    if (debugSerial) std::cout << "Serial port closed" << std::endl;
}

// --------------------
// SerialWrite
// --------------------
/**
 * Writes a single byte to the serial port
 *
 * @param byte The single byte to write
 * @return 1 if successful, 0 on error
 */
EXPORT int SerialWrite(unsigned char byte) {
    if (hSerial == INVALID_HANDLE_VALUE) {
        return 0;
    }

    DWORD bytesWritten;
    if (!WriteFile(hSerial, &byte, 1, &bytesWritten, NULL)) {
        return 0;
    }

    return (bytesWritten == 1) ? 1 : 0;
}

/**
 * Writes two bytes (position and speed) to the serial port
 *
 * @param position The position value to write
 * @param speed The speed value to write
 * @return The number of bytes written (2 on success, 0 on error)
 */
EXPORT int SerialWriteTwo(unsigned char position, unsigned char speed) {
    if (hSerial == INVALID_HANDLE_VALUE) {
        return 0;
    }

    unsigned char buf[2] = {position, speed};
    DWORD bytesWritten;

    if (!WriteFile(hSerial, buf, sizeof(buf), &bytesWritten, NULL)) {
        return 0;
    }

    return (bytesWritten == sizeof(buf)) ? (int)bytesWritten : 0;
}

// --------------------
// SerialRead - Read one byte
// Returns 0-255 for a byte, -1 when no data is available, -2 when the device is gone (unplugged / I/O error).
// --------------------
EXPORT int SerialRead() {
    if (hSerial == INVALID_HANDLE_VALUE) {
        return -1;  // Not connected
    }

    unsigned char byte = 0;
    DWORD bytesRead;

    if (!ReadFile(hSerial, &byte, 1, &bytesRead, NULL)) {
        // With the read timeouts set in SerialOpen, "no data" succeeds with 0 bytes; a failure means the device is gone.
        if (debugSerial) std::cerr << "Serial read error: " << GetLastError() << std::endl;
        return -2;
    }

    if (bytesRead == 1) {
        if (debugSerial) {
            std::cout << "Read byte: " << (int)byte << " (0x" << std::hex << (int)byte << std::dec << ")" << std::endl;
        }
        return (int)byte;  // Return as int (0-255)
    }

    // No data available
    return -1;  // Indicates no data
}
