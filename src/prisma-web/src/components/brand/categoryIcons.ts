import {
  Banknote, Beer, Bike, BookOpen, Briefcase, Building2, Bus, Car, CarTaxiFront, CircleDashed, Clapperboard,
  CreditCard, Croissant, Droplets, Dumbbell, Flame, Fuel, GraduationCap, HeartPulse, House, KeyRound, Landmark,
  Laptop, Layers, PartyPopper, Phone, PiggyBank, Pill, Plane, Presentation, Repeat, School, Shapes, Shirt,
  ShieldPlus, ShoppingBag, ShoppingCart, Smartphone, Sofa, SquareParking, Sprout, Stethoscope, Tv, Undo2,
  Utensils, UtensilsCrossed, Wifi, Wrench, Zap, type LucideIcon,
} from 'lucide-react'

// Ícones das categorias por nome (o mesmo nome kebab-case gravado pelo backend em
// DefaultCategories). Mapa fechado de propósito: importar o conjunto inteiro do Lucide
// colocaria mais de mil ícones no bundle.
const icons: Record<string, LucideIcon> = {
  'house': House, 'key-round': KeyRound, 'building-2': Building2, 'zap': Zap, 'droplets': Droplets,
  'wifi': Wifi, 'flame': Flame, 'utensils': Utensils, 'shopping-cart': ShoppingCart,
  'utensils-crossed': UtensilsCrossed, 'bike': Bike, 'croissant': Croissant, 'car': Car, 'fuel': Fuel,
  'car-taxi-front': CarTaxiFront, 'square-parking': SquareParking, 'wrench': Wrench, 'bus': Bus,
  'heart-pulse': HeartPulse, 'shield-plus': ShieldPlus, 'pill': Pill, 'stethoscope': Stethoscope,
  'dumbbell': Dumbbell, 'graduation-cap': GraduationCap, 'presentation': Presentation, 'book-open': BookOpen,
  'school': School, 'party-popper': PartyPopper, 'tv': Tv, 'plane': Plane, 'beer': Beer,
  'clapperboard': Clapperboard, 'shopping-bag': ShoppingBag, 'shirt': Shirt, 'smartphone': Smartphone,
  'sofa': Sofa, 'layers': Layers, 'repeat': Repeat, 'phone': Phone, 'landmark': Landmark, 'shapes': Shapes,
  'briefcase': Briefcase, 'laptop': Laptop, 'sprout': Sprout, 'undo-2': Undo2,
}

export function categoryIcon(name: string | null | undefined): LucideIcon {
  return (name && icons[name]) || CircleDashed
}

export const accountTypeIcons = {
  Checking: Landmark,
  CreditCard: CreditCard,
  Cash: Banknote,
  Investment: PiggyBank,
} as const
