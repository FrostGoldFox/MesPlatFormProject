using System;
using System.Collections.Generic;

var InfeedConveyor = new InfeedConveyorDataModel("InfeedConveyor");
var InspectionConveyor = new InspectionConveyorDataModel("InspectionConveyor");
var PneumaticPressureSensor = new PneumaticPressureDataModel("PneumaticPressureSensor");
var VibrationSensor = new VibrationDataModel("VibrationSensor");
var RejectCylinder = new RejectCylinderDataModel("RejectCylinder");

var Modbus = new ModbusNetWork('10.10.10.141', 7654);
var ct = new cancellationToken { IsCancellationRequested = true };
await Modbus.CreateMaster(ct);






















