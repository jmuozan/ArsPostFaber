import serial
import time
import sys

def setup_serial_connection(port, baud_rate=115200, timeout=2):
    """Set up a serial connection with the specified parameters."""
    try:
        ser = serial.Serial(port, baud_rate, timeout=timeout)
        print(f"Successfully connected to {port} at {baud_rate} baud")
        # Wait for printer to initialize
        time.sleep(2)
        # Clear any startup messages
        ser.reset_input_buffer()
        return ser
    except serial.SerialException as e:
        print(f"Error connecting to {port}: {e}")
        return None

def list_available_ports():
    """List all available serial ports on the system."""
    import serial.tools.list_ports
    ports = serial.tools.list_ports.comports()
    
    if not ports:
        print("No serial ports detected.")
        return
    
    print("Available serial ports:")
    for i, port in enumerate(ports):
        print(f"  {i+1}. {port.device} - {port.description}")

def send_gcode(ser, command):
    """Send a G-code command and wait for response."""
    try:
        # Format command with proper line ending
        if not command.endswith('\r\n'):
            command = command.strip() + '\r\n'
        
        # Send command
        ser.write(command.encode())
        ser.flush()
        print(f"Sent: {command.strip()}")
        
        # Wait for and read response
        time.sleep(0.5)  # Give more time for the printer to process
        
        response = ""
        start_time = time.time()
        
        # Keep reading until we get an 'ok' or timeout
        while time.time() - start_time < 5:  # 5 second timeout
            if ser.in_waiting:
                line = ser.readline().decode('latin-1').strip()
                if line:
                    print(f"Received: {line}")
                    response += line + "\n"
                    # Many printers send "ok" when command is processed
                    if line.startswith("ok"):
                        break
            time.sleep(0.1)
            
        if not response:
            print("No response received (this might be normal for some commands)")
            
        return response
    except Exception as e:
        print(f"Error sending command: {e}")
        return None

def main():
    # List available ports first
    list_available_ports()
    
    # Get port from user
    port = input("\nEnter the serial port (e.g., 'COM3' or '/dev/ttyUSB0'): ")
    
    # Get baud rate from user with a higher default
    try:
        baud_rate = int(input("Enter baud rate (default: 115200): ") or "115200")
    except ValueError:
        print("Invalid baud rate, using default: 115200")
        baud_rate = 115200
    
    # Setup connection
    ser = setup_serial_connection(port, baud_rate)
    if not ser:
        print("Failed to connect. Please check your port and try again.")
        sys.exit(1)
    
    print("\n==== 3D Printer Terminal ====")
    print("Type G-code commands to send (press Ctrl+C to exit)")
    print("Common commands: G28 (Home), M114 (Get Position), M119 (Endstop Status)")
    print("==============================\n")
    
    try:
        # Initialize with M115 to get printer info
        print("Requesting printer information...")
        send_gcode(ser, "M115")
        
        while True:
            command = input("> ")
            if command.lower() in ['exit', 'quit']:
                break
            if command.strip():
                send_gcode(ser, command)
    except KeyboardInterrupt:
        print("\nExiting...")
    finally:
        if ser and ser.is_open:
            ser.close()
            print("Serial connection closed.")

if __name__ == "__main__":
    main()