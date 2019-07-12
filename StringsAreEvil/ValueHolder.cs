namespace StringsAreEvil
{
    public  struct ValueHolder
    {
        public  int ElementId ;
        public  int VehicleId ;
        public  int Term ;
        public  int Mileage ;
        public  decimal Value;

        
        public void Set(int elementId, int vehicleId, int term, int mileage, decimal value)
        {
            ElementId = elementId; 
            VehicleId = vehicleId;
            Term = term;
            Mileage   = mileage;
            Value     = value;
        }

        public override string ToString() => ElementId + "," + VehicleId + "," + Term + "," + Mileage + "," + Value;
    }
}