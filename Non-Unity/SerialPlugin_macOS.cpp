// SerialPlugin_macOS.cpp
// macOS implementation of Unity Serial Plugin
// Rename file to SerialPlugin_macOS.cpp (stored without because otherwise Unity cannot build) 
//
// Compile with clang++:
// clang++ -dynamiclib -o SerialPlugin.bundle SerialPlugin_macOS.cpp -framework IOKit -framework CoreFoundation
//
// Compile with clang++ inc source file handling:
// cp SerialPlugin_macOS SerialPlugin_macOS.cpp && clang++ -dynamiclib -o SerialPlugin.bundle SerialPlugin_macOS.cpp -framework IOKit -framework CoreFoundation && rm SerialPlugin_macOS.cpp
//
// Place the resulting SerialPlugin.dll in a /Plugins folder in Unity

#include <termios.h>
#include <unistd.h>
#include <fcntl.h>
#include <errno.h>
#include <string.h>
#include <iostream>

#define EXPORT extern "C"

// --------------------
// State
// --------------------
static int fd = -1;
static bool debugSerial = false;

// --------------------
// Needed on MacOS because termios uses the type `speed_t` as baud rate.
// Initially the switch-case was using int or long as input (e.g. `case 4800: return B4800;`)
// However, this never resulted in a positive match, and always returned the `default` value.
// Below is a bit of a workaround: unsigned char / byte always gets evaluated correctly, making it possible to get it to return the correct baud rate. 
// Unity has these values stored in an enum, making it possible to switch baud when required.
// --------------------
static speed_t baudToSpeed(unsigned char baudIndex) {
    switch (baudIndex) {
        case 0:     return B4800;
        case 1:     return B9600;
        case 2:     return B19200;
        case 3:     return B38400;
        case 4:     return B57600;
        case 5:     return B115200;
        default:    return B300;
    }
}

// --------------------
// SerialOpen
// --------------------
EXPORT int SerialOpen(const char* portName, unsigned char baudIndex, bool debug) {
    debugSerial = debug;

    fd = open(portName, O_RDWR | O_NOCTTY | O_NONBLOCK);

    if (fd < 0) {
        if (debugSerial) std::cerr << "Failed to open port" << std::endl;
        return 0;
    }

    struct termios options;
    tcgetattr(fd, &options);

    speed_t spd = baudToSpeed(baudIndex);

    cfsetispeed(&options, spd);
    cfsetospeed(&options, spd);

    options.c_cflag |= CS8 | CLOCAL | CREAD;
    options.c_iflag = IGNPAR;
    options.c_oflag = 0;
    options.c_lflag = 0;

    // CRITICAL: Set non-blocking read behavior
    options.c_cc[VMIN] = 0;   // Return immediately with whatever is available
    options.c_cc[VTIME] = 0;  // No timeout

    tcflush(fd, TCIFLUSH);
    tcsetattr(fd, TCSANOW, &options);

    // Arduino CDC devices reboot on open → wait
    usleep(20000); // was initially set to 800000

    if (debugSerial) std::cout << "Serial port opened" << std::endl;
    return 1;
}

// --------------------
// SerialClose
// --------------------
EXPORT void SerialClose() {
    if (fd >= 0) close(fd);
    fd = -1;
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
    // Write the single byte
    int n = write(fd, &byte, 1);
    return (n == 1) ? 1 : 0;
}

/**
 * Writes two bytes (position and speed) to the serial port
 *
 * @param position The position value to write
 * @param speed The speed value to write
 * @return The number of bytes written (2 on success, 0 on error)
 */
EXPORT int SerialWriteTwo(unsigned char position, unsigned char speed) {
    // Create a temporary buffer on the stack to hold our two bytes
    // This avoids the need for a separate internal function
    unsigned char buf[2] = {position, speed};

    // Write the buffer to the serial port
    // Note: We're using write() which is POSIX-compliant and works on macOS
    int n = write(fd, buf, sizeof(buf));

    // Return 2 on success, 0 on error
    // write() returns -1 on error, so we convert that to 0
    return (n == sizeof(buf)) ? n : 0;
}

// --------------------
// SerialRead - Read one byte
// Returns 0-255 for a byte, -1 when no data is available, -2 when the device is gone (unplugged / I/O error).
// Plugins built before -2 existed returned -1 in that case too; the C# side handles both.
// --------------------
EXPORT int SerialRead() {
    if (fd < 0) {
        return -1;  // Not connected
    }
    
    unsigned char byte = 0;
    int n = read(fd, &byte, 1);
    
    if (n == 1) {
        if (debugSerial) {
            std::cout << "Read byte: " << (int)byte << " (0x" << std::hex << (int)byte << std::dec << ")" << std::endl;
        }
        return (int)byte;  // Return as int (0-255)
    }

    if (n < 0) {
        // The port is opened with O_NONBLOCK, so "no data" is EAGAIN / EWOULDBLOCK.
        if (errno == EAGAIN || errno == EWOULDBLOCK || errno == EINTR) {
            return -1;  // Indicates no data
        }

        if (debugSerial) std::cerr << "Serial read error: " << strerror(errno) << std::endl;
        return -2;  // EIO / ENXIO / ENODEV / EBADF: the device is gone
    }

    // n == 0: end of file. Make sure the device is really gone before reporting it.
    struct termios options;
    if (tcgetattr(fd, &options) != 0) {
        if (debugSerial) std::cerr << "Serial device disconnected" << std::endl;
        return -2;
    }

    // No data available
    return -1;  // Indicates no data
}
